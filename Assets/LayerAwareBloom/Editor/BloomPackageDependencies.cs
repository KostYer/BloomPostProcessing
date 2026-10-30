using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LayerAwareBloom.Editor
{
    // Unity's Asset Store exporter uses this archive entry for the native
    // Install/Upgrade prompt before asset import and script compilation.
    internal static class BloomPackageDependencies
    {
        internal const string Manifest = "{\n  \"dependencies\": {\n" +
            "    \"com.unity.render-pipelines.universal\": \"17.3.0\",\n" +
            "    \"com.unity.ugui\": \"2.0.0\",\n" +
            "    \"com.unity.inputsystem\": \"1.19.0\"\n  }\n}\n";

        // The input must be a fresh AssetDatabase.ExportPackage export, which
        // contains no Package Manager manifest. Preserve all its entries verbatim.
        internal static void WriteWithDependencies(string sourcePackage, string destinationPackage)
        {
            string temporary = destinationPackage + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var sourceFile = File.OpenRead(sourcePackage))
                using (var source = new GZipStream(sourceFile, CompressionMode.Decompress))
                using (var destinationFile = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var destination = new GZipStream(destinationFile, CompressionLevel.Optimal))
                {
                    byte[] content = Encoding.UTF8.GetBytes(Manifest);
                    byte[] header = CreateHeader("packagemanagermanifest/asset", content.Length);
                    destination.Write(header, 0, header.Length);
                    destination.Write(content, 0, content.Length);
                    int padding = (512 - content.Length % 512) % 512;
                    destination.Write(new byte[padding], 0, padding);
                    // Retain the original TAR end markers after the original assets.
                    source.CopyTo(destination);
                }

                if (File.Exists(destinationPackage)) File.Replace(temporary, destinationPackage, null);
                else File.Move(temporary, destinationPackage);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static byte[] CreateHeader(string name, int length)
        {
            var header = new byte[512];
            WriteText(header, 0, name);
            WriteOctal(header, 100, 8, 420); // 0644
            WriteOctal(header, 108, 8, 0);
            WriteOctal(header, 116, 8, 0);
            WriteOctal(header, 124, 12, length);
            WriteOctal(header, 136, 12, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            for (int i = 148; i < 156; i++) header[i] = (byte)' ';
            header[156] = (byte)'0';
            WriteText(header, 257, "ustar");
            WriteText(header, 263, "00");
            int checksum = 0;
            foreach (byte value in header) checksum += value;
            WriteOctal(header, 148, 7, checksum);
            return header;
        }

        private static void WriteOctal(byte[] header, int offset, int width, long value)
        {
            WriteText(header, offset, Convert.ToString(value, 8).PadLeft(width - 1, '0'));
            header[offset + width - 1] = 0;
        }

        private static void WriteText(byte[] header, int offset, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, 0, header, offset, bytes.Length);
        }
    }
}
