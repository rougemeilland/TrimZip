using System;
using System.Collections.Generic;
using System.Linq;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Compression.Archive.Zip;

namespace TrimZip.CUI
{
    internal sealed class IndexedZipEntries
        : IDisposable
    {
        private readonly ZipArchiveFileReader _reader;
        private readonly Dictionary<string, ZipSourceEntry> _entries;

        private bool _isDisposed;

        private IndexedZipEntries(ZipArchiveFileReader reader, Dictionary<string, ZipSourceEntry> entries)
        {
            _entries = entries;
            _isDisposed = false;
            _reader = reader;
        }

        public int Count
        {
            get
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(GetType().FullName);

                return _entries.Count;
            }
        }

        public ZipSourceEntry? this[string entryName]
        {
            get
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(GetType().FullName);

                return _entries.TryGetValue(entryName, out var entry) ? entry : null;
            }
        }

        public static IndexedZipEntries CreateInstance(FilePath zipArchiveFile)
        {
            var zipReader = zipArchiveFile.OpenAsZipFile();
            var entries = zipReader.EnumerateEntries().ToDictionary(entry => entry.FullName, entry => entry);
            return new IndexedZipEntries(zipReader, entries);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _reader.Dispose();
                }

                _isDisposed = true;
            }
        }
    }
}
