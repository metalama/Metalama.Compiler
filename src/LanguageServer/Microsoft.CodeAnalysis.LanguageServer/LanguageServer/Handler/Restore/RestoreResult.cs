// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

<<<<<<<< HEAD:src/Metalama/Metalama.Compiler.UnitTests.ThirdParty/ThirdPartyDummyTransformer.cs
namespace Metalama.Compiler.UnitTests.ThirdParty
{
    public class ThirdPartyDummyTransformer : ISourceTransformer
    {
        public void Execute(TransformerContext context)
        {
        }
    }
}
========
using System.Text.Json.Serialization;

namespace Microsoft.CodeAnalysis.LanguageServer.Handler;

internal sealed record RestoreResult(
    [property: JsonPropertyName("success")] bool Success
);
>>>>>>>> 2333221c0564852ee5ceac9c622a0b14e864d76e:src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/LanguageServer/Handler/Restore/RestoreResult.cs
