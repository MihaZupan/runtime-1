﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace System.Reflection
{
    public static class RoslynExtensions
    {
        public static Type AsType(this ITypeSymbol typeSymbol, MetadataLoadContext metadataLoadContext)
        {
            if (typeSymbol == null)
            {
                return null;
            }

            return new TypeWrapper(typeSymbol, metadataLoadContext);
        }

        public static MethodInfo AsMethodInfo(this IMethodSymbol methodSymbol, MetadataLoadContext metadataLoadContext) => (methodSymbol == null ? null : new MethodInfoWrapper(methodSymbol, metadataLoadContext))!;

        public static IEnumerable<INamedTypeSymbol> BaseTypes(this INamedTypeSymbol typeSymbol)
        {
            var t = typeSymbol;
            while (t != null)
            {
                yield return t;
                t = t.BaseType;
            }
        }
    }
}
