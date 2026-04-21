// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

using System.Reflection;

namespace System
{
    // Copyright @kzu
    // License MIT
    // copied from https://github.com/devlooped/ThisAssembly/blob/main/src/EmbeddedResource.cs
    /// <inheritdoc/>
    internal static class EmbeddedResource
    {
        internal static readonly string s_baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        /// <inheritdoc/>
        public static Stream GetStream(string relativePath)
        {
            var filePath = Path.Combine(s_baseDir, Path.GetFileName(relativePath));
            if (File.Exists(filePath))
            {
                return new MemoryStream(File.ReadAllBytes(filePath));
            }

            var baseName = Assembly.GetExecutingAssembly().GetName().Name;
            var resourceName = relativePath
                .TrimStart('.')
                .Replace(Path.DirectorySeparatorChar, '.')
                .Replace(Path.AltDirectorySeparatorChar, '.');

            var manifestResourceName = Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().FirstOrDefault(x => x!.EndsWith(resourceName, StringComparison.InvariantCulture));

            if (string.IsNullOrEmpty(manifestResourceName))
                throw new InvalidOperationException($"Did not find required resource ending in '{resourceName}' in assembly '{baseName}'.");

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(manifestResourceName) ?? throw new InvalidOperationException($"Did not find required resource '{manifestResourceName}' in assembly '{baseName}'.");
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        /// <inheritdoc/>
        public static string GetContent(string relativePath)
        {
            var filePath = Path.Combine(s_baseDir, Path.GetFileName(relativePath));
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            var baseName = Assembly.GetExecutingAssembly().GetName().Name;
            var resourceName = relativePath
                .TrimStart('.')
                .Replace(Path.DirectorySeparatorChar, '.')
                .Replace(Path.AltDirectorySeparatorChar, '.');

            var manifestResourceName = Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().FirstOrDefault(x => x!.EndsWith(resourceName, StringComparison.InvariantCulture));

            if (string.IsNullOrEmpty(manifestResourceName))
                throw new InvalidOperationException($"Did not find required resource ending in '{resourceName}' in assembly '{baseName}'.");

            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(manifestResourceName);

            if (stream == null)
            {
                throw new InvalidOperationException($"Did not find required resource '{manifestResourceName}' in assembly '{baseName}'.");
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
