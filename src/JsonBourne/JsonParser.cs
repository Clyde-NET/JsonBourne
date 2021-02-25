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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using JsonBourne.DocumentModel;

namespace JsonBourne
{
    /// <summary>
    /// Parses a JSON document and creates parsed JSON document trees. This class accepts UTF-8 inputs only.
    /// </summary>
    public sealed class JsonParser
    {
        public JsonValue Parse(ReadOnlyMemory<byte> input)
        {
            throw new NotImplementedException();
        }

        public async Task<JsonValue> ParseAsync(Stream input)
        {
            throw new NotImplementedException();
        }
    }
}
