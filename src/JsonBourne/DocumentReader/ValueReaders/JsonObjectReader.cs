// This file is a part of JsonBourne project.
// 
// Copyright 2020 Emzi0767
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
//   
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using JsonBourne.DocumentModel;

namespace JsonBourne.DocumentReader
{
    internal sealed class JsonObjectReader : IJsonValueReader<IReadOnlyDictionary<string, JsonValue>>
    {
        private int _lineSpan, _colSpan, _streamPos;
        private Dictionary<string, JsonValue> _obj;
        private ExpectedToken _expectedNext;
        private IJsonValueReader _innerReader;
        private readonly ValueReaderCollection _innerReaders;
        private string _currentKey;

        public JsonObjectReader(ValueReaderCollection valueReaders)
        {
            this._innerReaders = valueReaders;
            this.Reset();
        }

        public ValueParseResult TryParse(ReadOnlyMemory<byte> buffer, out IReadOnlyDictionary<string, JsonValue> result, out int consumedLength, out int lineSpan, out int colSpan)
            => this.TryParse(buffer.Span, out result, out consumedLength, out lineSpan, out colSpan);

        public ValueParseResult TryParse(ReadOnlySpan<byte> readerSpan, out IReadOnlyDictionary<string, JsonValue> result, out int consumedLength, out int lineSpan, out int colSpan)
        {
            result = null;
            consumedLength = 0;
            lineSpan = 1;
            colSpan = 0;

            // is input empty
            if (readerSpan.Length <= 0 && this._innerReader == null)
            {
                // did any prior processing occur
                return this._obj != null
                    ? _cleanup(this, ValueParseResult.FailureEOF)
                    : ValueParseResult.EOF;
            }

            // if we are not continuing, ensure it's an object that's being parsed
            if (this._obj == null)
            {
                if (readerSpan[consumedLength++] != JsonTokens.OpeningBrace)
                {
                    if (Rune.DecodeFromUtf8(readerSpan, out var rune, out _) != OperationStatus.Done)
                        rune = default;

                    return _cleanup(this, ValueParseResult.Failure("Unexpected token, expected {.", rune));
                }

                this._expectedNext = ExpectedToken.KeyOrEnd;
                this._obj = new Dictionary<string, JsonValue>();
                ++this._colSpan;
                ++this._streamPos;
            }

            // if continuing, check if any value is being parsed
            if (this._innerReader != null)
            {
                // valid only if expecting value
                if (this._expectedNext != ExpectedToken.Value && this._expectedNext != ExpectedToken.Key && this._expectedNext != ExpectedToken.KeyOrEnd)
                    return _cleanup(this, ValueParseResult.Failure("Invalid internal state.", default));

                // if key expected, inner parser must be a string parser
                if ((this._expectedNext == ExpectedToken.Key || this._expectedNext == ExpectedToken.KeyOrEnd) && this._innerReader is not JsonStringReader)
                    return _cleanup(this, ValueParseResult.Failure("Invalid internal state.", default));

                // parse inner value
                var expectKey = this._expectedNext == ExpectedToken.Key || this._expectedNext == ExpectedToken.KeyOrEnd;
                ++consumedLength;
                var innerResult = this.ParseInner(readerSpan, out var innerValue, ref consumedLength);
                switch (innerResult.Type)
                {
                    case ValueParseResultType.Success:
                        this._innerReader.Reset();
                        this._innerReader = null;

                        if (expectKey)
                        {
                            if (innerValue is not JsonStringValue jsonString)
                                return _cleanup(this, ValueParseResult.Failure("Invalid internal state.", default));

                            this._currentKey = jsonString.Value;
                        }
                        else
                        {
                            if (this._currentKey == null)
                                return _cleanup(this, ValueParseResult.Failure("Invalid internal state.", default));

                            this._obj[this._currentKey] = innerValue;
                            this._currentKey = null;
                        }

                        break;

                    case ValueParseResultType.EOF:
                        return innerResult;

                    case ValueParseResultType.Failure:
                        return _cleanup(this, innerResult);
                }
            }

            // read and parse object entries
            var completedParsing = false;
            while (consumedLength < readerSpan.Length)
            {
                switch (readerSpan[consumedLength++])
                {
                    case JsonTokens.WhitespaceSpace:
                        ++this._colSpan;
                        ++this._streamPos;
                        break;

                    case JsonTokens.WhitespaceHorizontalTab:
                        this._colSpan += 4; // fite me
                        ++this._streamPos;
                        break;

                    case JsonTokens.WhitespaceCarriageReturn:
                        // usually as part of CRLF, really no other reason for it to exist
                        // old macs don't exist
                        break;

                    case JsonTokens.WhitespaceNewline:
                        ++this._lineSpan;
                        this._colSpan = 0;
                        ++this._streamPos;
                        break;

                    case JsonTokens.KeyValueSeparator:
                        if (this._expectedNext != ExpectedToken.KvSeparator)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected key-value separator.", new Rune(JsonTokens.KeyValueSeparator)));

                        ++this._colSpan;
                        ++this._streamPos;
                        this._expectedNext = ExpectedToken.Value;
                        break;

                    case JsonTokens.ItemSeparator:
                        if (this._expectedNext != ExpectedToken.ItemSeparatorOrEnd)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected item separator.", new Rune(JsonTokens.ItemSeparator)));

                        ++this._colSpan;
                        ++this._streamPos;
                        this._expectedNext = ExpectedToken.Key;
                        break;

                    case JsonTokens.ClosingBrace:
                        if (this._expectedNext != ExpectedToken.ItemSeparatorOrEnd && this._expectedNext != ExpectedToken.KeyOrEnd)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected object end.", new Rune(JsonTokens.ClosingBrace)));

                        ++this._colSpan;
                        ++this._streamPos;
                        completedParsing = true;
                        break;

                    case JsonTokens.NullFirst:
                        if (this._expectedNext != ExpectedToken.Value)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected object item (null).", new Rune(JsonTokens.NullFirst)));

                        this._innerReader = this._innerReaders.NullReader;
                        break;

                    case JsonTokens.TrueFirst:
                    case JsonTokens.FalseFirst:
                        if (this._expectedNext != ExpectedToken.Value)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected object item (boolean).", new Rune(readerSpan[consumedLength - 1])));

                        this._innerReader = this._innerReaders.BooleanReader;
                        break;

                    case JsonTokens.NumberSign:
                    case JsonTokens.Digit0:
                    case JsonTokens.Digit1:
                    case JsonTokens.Digit2:
                    case JsonTokens.Digit3:
                    case JsonTokens.Digit4:
                    case JsonTokens.Digit5:
                    case JsonTokens.Digit6:
                    case JsonTokens.Digit7:
                    case JsonTokens.Digit8:
                    case JsonTokens.Digit9:
                        if (this._expectedNext != ExpectedToken.Value)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected object item (number).", new Rune(readerSpan[consumedLength - 1])));

                        this._innerReader = this._innerReaders.NumberReader;
                        break;

                    case JsonTokens.QuoteMark:
                        if (this._expectedNext != ExpectedToken.Value && this._expectedNext != ExpectedToken.Key && this._expectedNext != ExpectedToken.KeyOrEnd)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected object item (string).", new Rune(JsonTokens.QuoteMark)));

                        this._innerReader = this._innerReaders.StringReader;
                        break;

                    case JsonTokens.OpeningBracket:
                        if (this._expectedNext != ExpectedToken.Value)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected object item (array).", new Rune(JsonTokens.OpeningBracket)));

                        this._innerReader = new JsonArrayReader(this._innerReaders);
                        break;

                    case JsonTokens.OpeningBrace:
                        if (this._expectedNext != ExpectedToken.Value)
                            return _cleanup(this, ValueParseResult.Failure("Unexpected object item (object).", new Rune(JsonTokens.OpeningBracket)));

                        this._innerReader = new JsonObjectReader(this._innerReaders);
                        break;

                    default:
                        if (Rune.DecodeFromUtf8(readerSpan[(consumedLength - 1)..], out var rune, out _) != OperationStatus.Done)
                            rune = default;

                        return _cleanup(this, ValueParseResult.Failure("Unexpected token while parsing object.", rune));
                }

                // parsing done?
                if (completedParsing)
                    break;

                // parse inner value
                if (this._innerReader != null)
                {
                    var expectKey = this._expectedNext == ExpectedToken.Key || this._expectedNext == ExpectedToken.KeyOrEnd;
                    var innerResult = this.ParseInner(readerSpan.Slice(consumedLength - 1), out var innerValue, ref consumedLength);
                    switch (innerResult.Type)
                    {
                        case ValueParseResultType.Success:
                            this._innerReader.Reset();
                            this._innerReader = null;

                            if (expectKey)
                            {
                                if (innerValue is not JsonStringValue jsonString)
                                    return _cleanup(this, ValueParseResult.Failure("Invalid internal state.", default));

                                this._currentKey = jsonString.Value;
                            }
                            else
                            {
                                if (this._currentKey == null)
                                    return _cleanup(this, ValueParseResult.Failure("Invalid internal state.", default));

                                this._obj[this._currentKey] = innerValue;
                                this._currentKey = null;
                            }

                            break;

                        case ValueParseResultType.EOF:
                            return innerResult;

                        case ValueParseResultType.Failure:
                            return _cleanup(this, innerResult);
                    }
                }
            }

            // did we reach the end of input before running out of it
            if (completedParsing)
            {
                colSpan = this._colSpan;
                lineSpan = this._lineSpan;
                result = new ReadOnlyDictionary<string, JsonValue>(this._obj);
                return _cleanup(this, ValueParseResult.Success);
            }
            // no, yield back
            {
                return ValueParseResult.EOF;
            }

            static ValueParseResult _cleanup(IJsonValueReader rdr, ValueParseResult result)
            {
                rdr.Reset();
                return result;
            }
        }

        public void Dispose()
        {
            this.Reset();
            this._innerReaders.Dispose();
        }

        public void Reset()
        {
            this._innerReader?.Reset();
            this._innerReader = null;
            this._obj = null;
            this._streamPos = 0;
            this._lineSpan = 1;
            this._colSpan = 0;
            this._expectedNext = ExpectedToken.None;
            this._currentKey = null;
        }

        private ValueParseResult ParseInner(ReadOnlySpan<byte> input, out JsonValue resultValue, ref int consumedBytes)
        {
            ValueParseResult innerResult;
            int innerLineSpan, innerColSpan, innerConsumed;
            resultValue = null;
            switch (this._innerReader)
            {
                case JsonNullReader nullReader:
                    {
                        innerResult = nullReader.TryParse(input, out _, out innerConsumed, out innerLineSpan, out innerColSpan);
                        if (innerResult.Type == ValueParseResultType.Success)
                            resultValue = JsonNullValue.Null;
                    }
                    break;

                case JsonBooleanReader boolReader:
                    {
                        innerResult = boolReader.TryParse(input, out var innerResultValue, out innerConsumed, out innerLineSpan, out innerColSpan);
                        if (innerResult.Type == ValueParseResultType.Success)
                            resultValue = innerResultValue ? JsonBooleanValue.True : JsonBooleanValue.False;
                    }
                    break;

                case JsonNumberReader numberReader:
                    {
                        innerResult = numberReader.TryParse(input, out var innerResultValue, out innerConsumed, out innerLineSpan, out innerColSpan);
                        if (innerResult.Type == ValueParseResultType.Success)
                            resultValue = new JsonNumberValue(innerResultValue);
                    }
                    break;

                case JsonStringReader stringReader:
                    {
                        innerResult = stringReader.TryParse(input, out var innerResultValue, out innerConsumed, out innerLineSpan, out innerColSpan);
                        if (innerResult.Type == ValueParseResultType.Success)
                            resultValue = new JsonStringValue(innerResultValue);
                    }
                    break;

                case JsonArrayReader arrayReader:
                    {
                        innerResult = arrayReader.TryParse(input, out var innerResultValue, out innerConsumed, out innerLineSpan, out innerColSpan);
                        if (innerResult.Type == ValueParseResultType.Success)
                            resultValue = new JsonArrayValue(innerResultValue);
                    }
                    break;

                case JsonObjectReader objectReader:
                    {
                        innerResult = objectReader.TryParse(input, out var innerResultValue, out innerConsumed, out innerLineSpan, out innerColSpan);
                        if (innerResult.Type == ValueParseResultType.Success)
                            resultValue = new JsonObjectValue(innerResultValue);
                    }
                    break;

                default:
                    return ValueParseResult.Failure("Unexpected inner reader type.", default);
            }

            this._streamPos += innerConsumed;
            consumedBytes += innerConsumed - 1;

            switch (innerResult.Type)
            {
                // inner parser concluded its operation
                // append its result, advance state
                case ValueParseResultType.Success:
                    this._lineSpan += innerLineSpan - 1;
                    this._colSpan = innerLineSpan > 1
                        ? innerColSpan
                        : this._colSpan + innerColSpan;

                    this._expectedNext = this._expectedNext switch
                    {
                        ExpectedToken.Value => ExpectedToken.ItemSeparatorOrEnd,
                        ExpectedToken.Key or ExpectedToken.KeyOrEnd => ExpectedToken.KvSeparator
                    };
                    return ValueParseResult.Success;

                // inner parser ran out of input
                case ValueParseResultType.EOF:
                    return ValueParseResult.EOF;

                // inner parser failed
                case ValueParseResultType.Failure:
                default:
                    this._lineSpan += innerLineSpan - 1;
                    this._colSpan = innerLineSpan > 1
                        ? innerColSpan
                        : this._colSpan + innerColSpan;
                    return innerResult.Enrich(this._streamPos, this._lineSpan - 1, this._colSpan);
            }
        }

        private enum ExpectedToken
        {
            None,
            ItemSeparatorOrEnd,
            Value,
            Key,
            KeyOrEnd,
            KvSeparator
        }
    }
}
