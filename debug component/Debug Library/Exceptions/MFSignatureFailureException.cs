//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Microsoft .NET Micro Framework and is unsupported. 
// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use these files except in compliance with the License.
// You may obtain a copy of the License at:
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing
// permissions and limitations under the License.
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using Windows.Storage;

namespace Microsoft.NetMicroFramework.Tools
{
    internal class MFSignatureFailureException : Exception
    {
        private StorageFile signatureFile;

        public MFSignatureFailureException()
        {
        }

        public MFSignatureFailureException(string message) : base(message)
        {
        }

        public MFSignatureFailureException(StorageFile signatureFile)
        {
            this.signatureFile = signatureFile;
        }

        public MFSignatureFailureException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
