using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace OmniSharp.Bsp
{
    public class CachingScriptMetadataResolver() : MetadataReferenceResolver
    {
        private static readonly MetadataReferenceResolver DefaultRuntimeResolver = ScriptMetadataResolver.Default;
        private static readonly ConcurrentDictionary<string, ImmutableArray<PortableExecutableReference>> DirectReferenceCache = new();
        private static readonly ConcurrentDictionary<string, PortableExecutableReference> MissingReferenceCache = new();

        public override bool Equals(object? other)
        {
            return DefaultRuntimeResolver.Equals(other);
        }

        public override int GetHashCode()
        {
            return DefaultRuntimeResolver.GetHashCode();
        }

        public override bool ResolveMissingAssemblies => DefaultRuntimeResolver.ResolveMissingAssemblies;

        public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity)
        {
            if (MissingReferenceCache.ContainsKey(referenceIdentity.Name))
            {
                return MissingReferenceCache[referenceIdentity.Name];
            }

            var result = DefaultRuntimeResolver.ResolveMissingAssembly(definition, referenceIdentity);
            if (result != null)
            {
                MissingReferenceCache[referenceIdentity.Name] = result;
            }

            return result;
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties)
        {
            var key = $"{reference}-{baseFilePath}";
            if (DirectReferenceCache.ContainsKey(key))
            {
                return DirectReferenceCache[key];
            }

            var result = DefaultRuntimeResolver.ResolveReference(reference, baseFilePath, properties);
            if (result.Length > 0)
            {
                DirectReferenceCache[key] = result;
            }

            return result;
        }
    }
}
