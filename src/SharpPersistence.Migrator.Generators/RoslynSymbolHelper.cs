using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPersistence.Migrator.Constants;

namespace SharpPersistence.Migrator.Generators;

internal static class RoslynSymbolHelper
{
    public static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol targetBase)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, targetBase))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    public static bool InheritsGenericDefinition(INamedTypeSymbol? type, INamedTypeSymbol genericDef)
    {
        var current = type;
        while (current is not null)
        {
            if (current.IsGenericType && SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, genericDef))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    public static bool IsMarkedReadOnly(
        INamedTypeSymbol entitySym,
        INamedTypeSymbol? defaultEntityAttr,
        INamedTypeSymbol? genericEntityAttr)
    {
        if (defaultEntityAttr is null && genericEntityAttr is null)
        {
            return false;
        }

        foreach (var attr in entitySym.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
            {
                continue;
            }

            var isEntityAttr =
                (defaultEntityAttr is not null && SymbolEqualityComparer.Default.Equals(attrClass, defaultEntityAttr)) ||
                (genericEntityAttr is not null && attrClass.IsGenericType &&
                 SymbolEqualityComparer.Default.Equals(attrClass.OriginalDefinition, genericEntityAttr));

            if (isEntityAttr && GetNamedBool(attr, KnownNames.AttributeProperties.ReadOnly, out _))
            {
                return true;
            }
        }

        return false;
    }

    public static bool GetNamedBool(AttributeData attr, string name, out bool isExplicitlySet)
    {
        foreach (var kv in attr.NamedArguments)
        {
            if (string.Equals(kv.Key, name, StringComparison.Ordinal))
            {
                isExplicitlySet = true;
                return kv.Value.Value is true;
            }
        }

        isExplicitlySet = false;
        return false;
    }

    public static bool IsDeclaredPartial(INamedTypeSymbol symbol)
    {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is ClassDeclarationSyntax cds &&
                cds.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }

        return false;
    }
}
