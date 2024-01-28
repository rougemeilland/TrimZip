using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Palmtree;
using Palmtree.Application;
using Palmtree.IO;
using Palmtree.IO.Compression.Archive.Zip;
using Palmtree.IO.Compression.Stream.Plugin.SevenZip;
using Palmtree.IO.Console;

namespace TrimZip
{
    public class TrimZipApplication
        : BatchApplication
    {
        private class EpubPackageDocumentSummary
        {
            public EpubPackageDocumentSummary((string Name, string? FileAs) title, IEnumerable<(string Name, string? FileAs, string? Role, string? RoleScheme, int? DisplaySeq)> creators, (string Name, string? FileAs)? publisher, string language, DateTimeOffset? modified, IEnumerable<string> subjects, string? description)
            {
                Title = title;
                Creators = creators.ToList();
                Publisher = publisher;
                Language = language;
                Modified = modified;
                Subjects = subjects;
                Description = description;
            }

            public (string Name, string? FileAs) Title { get; }
            public IEnumerable<(string Name, string? FileAs, string? Role, string? RoleScheme, int? DisplaySeq)> Creators { get; }
            public (string Name, string? FileAs)? Publisher { get; }
            public string Language { get; }
            public DateTimeOffset? Modified { get; }
            public IEnumerable<string> Subjects { get; }
            public string? Description { get; }
        }

        private const string _epubMimeTypeFileName = "mimetype";
        private const string _epubMimeType = "application/epub+zip";
        private const string _epubContainerFileName = "META-INF/container.xml";
        private const string _epubContainerAttributeFullPath = "full-path";
        private const string _epubContainerAttributeMediaType = "media-type";
        private const string _epubMediaTypeOfPackageDocument = "application/oebps-package+xml";

        private static readonly ReadOnlyMemory<byte> _epubMimeTypeFileNameBytes;
        private static readonly ReadOnlyMemory<byte> _epubMimeTypeBytes;

        private readonly string? _title;
        private readonly Encoding? _encoding;

        static TrimZipApplication()
        {
            _epubMimeTypeFileNameBytes = Encoding.ASCII.GetBytes(_epubMimeTypeFileName);
            _epubMimeTypeBytes = Encoding.ASCII.GetBytes(_epubMimeType);

            Bzip2CoderPlugin.EnablePlugin();
            DeflateCoderPlugin.EnablePlugin();
            Deflate64CoderPlugin.EnablePlugin();
            LzmaCoderPlugin.EnablePlugin();
        }

        public TrimZipApplication(string? title, Encoding? encoding)
        {
            _title = title;
            _encoding = encoding;
        }

        protected override string ConsoleWindowTitle => _title ?? base.ConsoleWindowTitle;
        protected override Encoding? InputOutputEncoding => _encoding;
        protected override bool DelayBreak => base.DelayBreak;

        protected override ResultCode Main(string[] args)
        {
            try
            {
                ReportProgress("Searching files...");
                var zipFiles =
                    args.EnumerateFilesFromArgument(true)
                        .Where(file => string.Equals(file.Extension, ".epub", StringComparison.OrdinalIgnoreCase) && CheckIfValidFilePath(file))
                        .ToList();
                var totalSize = zipFiles.Aggregate(0UL, (size, file) => checked(size + file.Length));
                var processedSize = 0UL;
                ReportProgress((double)processedSize / totalSize);
                foreach (var zipFile in zipFiles)
                {
                    if (IsPressedBreak)
                        return ResultCode.Cancelled;
                    var outputFileName = zipFile.Name;
                    var tempFile = new FilePath(Path.GetTempFileName());
                    try
                    {

                        var actualLength = TrimZip(zipFile, tempFile);
                        if (IsPressedBreak)
                            return ResultCode.Cancelled;
                        var destinationZipFileName = RenameZipFile(tempFile, zipFile.Directory, zipFile.Extension);
                        if (destinationZipFileName is not null)
                            outputFileName = destinationZipFileName;
                        checked
                        {
                            totalSize -= zipFile.Length;
                            totalSize += actualLength;
                            processedSize += actualLength;
                        }
                    }
                    catch (Exception ex)
                    {
                        TinyConsole.Erase(ConsoleEraseMode.FromCursorToEndOfLine);
                        ReportException(ex);
                    }
                    finally
                    {
                        tempFile.SafetyDelete();
                    }

                    ReportProgress((double)processedSize / totalSize, outputFileName, (percentage, content) => $"{percentage}: processing \"{content}\"");

                    ++processedSize;
                }

                return ResultCode.Success;
            }
            catch (Exception ex)
            {
                ReportException(ex);
                return ResultCode.Failed;
            }
        }

        protected override void Finish(ResultCode result)
        {
            if (result == ResultCode.Success)
                TinyConsole.WriteLine("Completed.");
            else if (result == ResultCode.Cancelled)
                TinyConsole.WriteLine("Cancelled.");

            base.Finish(result);
        }

        private static ulong TrimZip(FilePath sourceZipFile, FilePath destinationZipFile)
        {
            var arrayOfLength = EnumerateLengthOfZipFile(sourceZipFile).ToArray();
            if (arrayOfLength.Length <= 0)
                throw new Exception($"It is not a ZIP format file.: \"{sourceZipFile.FullName}\"");
            var actualLength = arrayOfLength[0];
            if (sourceZipFile.Length > actualLength)
            {
                using var inStream = sourceZipFile.OpenRead().AsSequentialAccess().WithPartial(actualLength);
                using var outStream = destinationZipFile.Create();
                inStream.CopyTo(outStream);
            }
            else
            {
                sourceZipFile.CopyTo(destinationZipFile, true);
            }

            return actualLength;
        }

        private static IEnumerable<ulong> EnumerateLengthOfZipFile(FilePath zipFile)
        {
            using var inStream = zipFile.OpenRead();
            var signatureBuffer = new (ulong pos, byte value)[4];
            using var enumerator = inStream.EnumerateBytesInReverseOrder().GetEnumerator();
            for (var index = 0; index < 4; ++index)
            {
                if (!enumerator.MoveNext())
                {
                    // ファイルの長さが 4 バイト未満である場合

                    // ファイルが短すぎるので列挙を中断する
                    yield break;
                }

                signatureBuffer[index] = enumerator.Current;
            }

            if (signatureBuffer[0].value == 0 && signatureBuffer[1].value == 0 && signatureBuffer[2].value == 0 && signatureBuffer[0].value == 3)
            {
                // 最初の 4 バイトがすべて 0 である場合

                // 連続する 0 をスキップする
                while (enumerator.MoveNext())
                {
                    signatureBuffer[0] = signatureBuffer[1];
                    signatureBuffer[1] = signatureBuffer[2];
                    signatureBuffer[2] = signatureBuffer[3];
                    signatureBuffer[3] = enumerator.Current;
                    if (signatureBuffer[3].value != 0)
                        break;
                }
            }

            const int MAXIMUM_EOCDR_LENGTH = 22 + ushort.MaxValue;

            // 0でないデータが見つかったら、EOCDR のシグニチャを探す
            var count = 0;
            while (count < MAXIMUM_EOCDR_LENGTH && enumerator.MoveNext())
            {
                signatureBuffer[0] = signatureBuffer[1];
                signatureBuffer[1] = signatureBuffer[2];
                signatureBuffer[2] = signatureBuffer[3];
                signatureBuffer[3] = enumerator.Current;
                if (CheckSignature(signatureBuffer))
                {
                    // EOCDR の シグニチャを発見
                    var positionOfEOCDR = signatureBuffer[3].pos;

                    // シグニチャからファイルの末尾まで読み取る
                    inStream.Seek(positionOfEOCDR);
                    var eocdrBufferLength = checked((int)(inStream.EndOfThisStream - positionOfEOCDR));
                    var eocdrBuffer = inStream.ReadBytes(checked((int)(inStream.EndOfThisStream - positionOfEOCDR)));
                    Validation.Assert(eocdrBuffer.Length == eocdrBufferLength, "eocdrBuffer.Length == eocdrBufferLength");
                    Validation.Assert(eocdrBuffer.Length >= 22, "eocdrBuffer.Length >= 22");
                    Validation.Assert(
                        eocdrBuffer.Span[0] == 0x50 && eocdrBuffer.Span[1] == 0x4b && eocdrBuffer.Span[2] == 0x05 && eocdrBuffer.Span[3] == 0x06,
                        "eocdrBuffer.Span[0] == 0x50 && eocdrBuffer.Span[1] == 0x4b && eocdrBuffer.Span[2] == 0x05 && eocdrBuffer.Span[3] == 0x06");

                    // EOCDR の後がすべて 0 であることを確認する
                    var commentLength = eocdrBuffer.Slice(20, 2).ToUInt16LE();
                    var lengthOfEOCDR = 22 + commentLength;

                    var allZero = true;
                    for (var index = lengthOfEOCDR; index < eocdrBuffer.Length; ++index)
                    {
                        if (eocdrBuffer.Span[index] != 0)
                            allZero = false;
                    }

                    if (allZero)
                    {
                        yield return checked(positionOfEOCDR + (ulong)lengthOfEOCDR);
                        yield break;
                    }
                }

                ++count;
            }
        }

        private static bool CheckSignature(Span<(ulong pos, byte value)> signatureBuffer)
            => signatureBuffer[0].value == 0x06
                && signatureBuffer[1].value == 0x05
                && signatureBuffer[2].value == 0x4b
                && signatureBuffer[3].value == 0x50;

        private static bool CheckIfValidFilePath(FilePath file)
            => !file.Name.StartsWith('.')
                && CheckIfValidDirectoryPath(file.Directory);

        private static bool CheckIfValidDirectoryPath(DirectoryPath directory)
        {
            for (var dir = directory; dir is not null; dir = dir.Parent)
            {
                if (dir.Name.StartsWith('.'))
                    return false;
            }

            return true;
        }

        private static string? RenameZipFile(FilePath sourceZipFile, DirectoryPath destinationDirectory, string sourceZipFileExtension)
        {
            if (!IsEpubFile(sourceZipFile))
                return null;

            using var zipReader = sourceZipFile.OpenAsZipFile();
            var entries = zipReader.EnumerateEntries().ToDictionary(entry => entry.FullName, entry => entry);
            if (entries.Count <= 0)
                return null;
            if (!entries.TryGetValue(_epubMimeTypeFileName, out var mimeTypeEntry))
                throw new Exception($".epub ファイルに \"{_epubMimeTypeFileName}\" が見つかりません。");
            using (var inStream = mimeTypeEntry.OpenContentStream())
            using (var reader = inStream.AsTextReader())
            {
                if (reader.ReadToEnd() != _epubMimeType)
                    return null;
            }

            if (!entries.TryGetValue(_epubContainerFileName, out var containerEntry))
                throw new Exception($".epub ファイルに \"{_epubContainerFileName}\" が見つかりません。");

            var packageDocumentFileName = ParseContainerXml(containerEntry).First();
            if (!entries.TryGetValue(packageDocumentFileName, out var packageDocumentEntry))
                throw new Exception($".epub ファイルに \"{packageDocumentFileName}\" が見つかりません。");

            var summary = ParsePackageDocument(packageDocumentEntry);

            var destinationFileName =
                new string(
                    $"[{string.Join("×", summary.Creators.Select(creator => creator.Name))}] {summary.Title.Name}{sourceZipFileExtension}"
                    .Select(c =>
                        c switch
                        {
                            >= '０' and <= '９' => (char)(c - '０' + '0'),
                            >= 'Ａ' and <= 'Ｚ' => (char)(c - 'Ａ' + 'A'),
                            >= 'ａ' and <= 'ｚ' => (char)(c - 'ａ' + 'a'),
                            '　' => ' ',
                            '！' => '!',
                            '＃' => '#',
                            '＄' => '$',
                            '％' => '%',
                            '＆' => '&',
                            '’' => '\'',
                            '（' => '(',
                            '）' => ')',
                            '＝' => '=',
                            '‐' => '-',
                            '＾' => '^',
                            '＠' => '@',
                            '‘' => '`',
                            '［' => '[',
                            '］' => ']',
                            '｛' => '{',
                            '｝' => '}',
                            '＋' => '+',
                            '＊' => '*',
                            '；' => ';',
                            '，' => ',',
                            '．' => '.',
                            '＿' => '_',
                            _ => c,
                        })
                    .ToArray())
                .WindowsFileNameEncoding();
            var destinationFile = destinationDirectory.GetFile(destinationFileName);
            if (!destinationFile.Exists)
            {
                sourceZipFile.CopyTo(destinationFile, false);
                return destinationFileName;
            }

            for (var count = 2; ; ++count)
            {
                var newDestinationFileName = $"{Path.GetFileNameWithoutExtension(destinationFileName)}__{count}{Path.GetExtension(destinationFileName)}";
                var newDestinationFile = destinationDirectory.GetFile(newDestinationFileName);
                if (!newDestinationFile.Exists)
                {
                    sourceZipFile.CopyTo(newDestinationFile, false);
                    return newDestinationFileName;
                }
            }
        }

        private static bool IsEpubFile(FilePath file)
        {
            using var inStream = file.OpenRead();
            Span<byte> buffer = stackalloc byte[30 + _epubMimeTypeFileName.Length];
            var length = inStream.Read(buffer);
            if (length != buffer.Length)
                return false;

            // シグニチャのチェック
            if (buffer[..4].ToUInt32LE() != 0x04034b50)
                return false;

            // フラグのチェック (EFS 以外は不許可)
            if ((buffer.Slice(6, 2).ToUInt16LE() & ~(1 << 11)) != 0)
                return false;

            // 圧縮方式のチェック (stored 以外は不許可)
            if (buffer.Slice(8, 2).ToUInt16LE() != 0)
                return false;

#if false
            // 圧縮済みサイズのチェック
            if (buffer.Slice(18, 4).ToUInt32LE() != _epubMimeTypeBytes.Length)
                return false;

            // 非圧縮サイズのチェック
            if (buffer.Slice(22, 4).ToUInt32LE() != _epubMimeTypeBytes.Length)
                return false;
#endif

            // ファイル名の長さのチェック
            if (buffer.Slice(26, 2).ToUInt16LE() != _epubMimeTypeFileNameBytes.Length)
                return false;

            var extraFieldLength = buffer.Slice(28, 2).ToUInt16LE();

            // ファイル名のチェック
            if (!buffer.Slice(30, _epubMimeTypeFileNameBytes.Length).SequenceEqual(_epubMimeTypeFileNameBytes.Span))
                return false;

#if false
            // 拡張フィールドを読み捨てる
            if (inStream.ReadBytes(extraFieldLength).Length != extraFieldLength)
                return false;

            // ファイルの内容を読み込む
            var content = inStream.ReadBytes(_epubMimeTypeBytes.Length);
            if (content.Length != _epubMimeTypeBytes.Length)
                return false;

            // MIME-TYPE のチェック
            if (!content.Span.SequenceEqual(_epubMimeTypeBytes.Span))
                return false;
#endif

            return true;
        }

        public static IEnumerable<string> ParseContainerXml(ZipSourceEntry containerEntry)
        {
            using var containerStream = containerEntry.OpenContentStream();
            using var containerReader = containerStream.AsTextReader(Encoding.UTF8);
            var containerText = containerReader.ReadToEnd();
            var containerDocument =
                XDocument.Parse(containerText)
                ?? throw new Exception("\"META-INF/container.xml\" ファイルが XML 形式ではありません。");

            var rootFileElements = containerDocument.Descendants(XName.Get("rootfile", "urn:oasis:names:tc:opendocument:xmlns:container")).ToList();
            foreach (var rootFileElement in rootFileElements)
                yield return Parse(rootFileElement);

            static string Parse(XElement rootFileElement)
            {
                try
                {
                    var mediaTypeAttribute =
                        rootFileElement.Attribute(_epubContainerAttributeMediaType)
                        ?? throw new Exception($"\"META-INF/container.xml\" ファイルの \"rootfile\" 要素に \"{_epubContainerAttributeMediaType}\" 属性がありません。");
                    if (mediaTypeAttribute.Value.Trim() != _epubMediaTypeOfPackageDocument)
                        throw new Exception($"\"META-INF/container.xml\" ファイルの \"rootfile\" 要素の \"{_epubContainerAttributeMediaType}\" 属性が \"{mediaTypeAttribute.Value.Trim()}\" になっていますが、\"{_epubMediaTypeOfPackageDocument}\" でなければなりません。");
                    var fullPathAttribute =
                        rootFileElement.Attribute(_epubContainerAttributeFullPath)
                        ?? throw new Exception($"\"META-INF/container.xml\" ファイルの \"rootfile\" 要素に \"{_epubContainerAttributeFullPath}\" 属性がありません。");
                    return fullPathAttribute.Value.Trim();
                }
                catch (Exception ex)
                {
                    throw new Exception("\"META-INF/container.xml\" ファイルの解析に失敗しました。", ex);
                }
            }
        }

        private static EpubPackageDocumentSummary ParsePackageDocument(ZipSourceEntry packageDocumentEntry)
        {
            const string defaultNamespace = "http://www.idpf.org/2007/opf";
            const string dcNamespace = "http://purl.org/dc/elements/1.1/";

            using var packageDocumentStream = packageDocumentEntry.OpenContentStream();
            using var packageDocumentReader = packageDocumentStream.AsTextReader(Encoding.UTF8);
            var packageDocumentText = packageDocumentReader.ReadToEnd();
            var packageDocument =
                XDocument.Parse(packageDocumentText)
                ?? throw new Exception($"\"{packageDocumentEntry.FullName}\" ファイルが XML 形式ではありません。");

            try
            {
                var metadataElements =
                    packageDocument.Descendants(XName.Get("meta", defaultNamespace))
                    .ToList();

                var titles =
                    packageDocument.Descendants(XName.Get("title", dcNamespace))
                    .Select(titleElement =>
                    {
                        var name = titleElement.Value.Trim();
                        var id = titleElement.Attribute("id")?.Value.Trim();
                        var propertyElements =
                            id is null
                            ? Array.Empty<XElement>()
                            : metadataElements
                                .Where(metadataElement => metadataElement.Attribute("refines")?.Value.Trim() == $"#{id}")
                                .ToList()
                                .AsEnumerable();
                        var asFileElement =
                           propertyElements
                           .Where(propertyElement => propertyElement.Attribute("property")?.Value.Trim() == "file-as")
                           .SingleOrDefault();

                        var titleTypeElement =
                           propertyElements
                           .Where(propertyElement => propertyElement.Attribute("property")?.Value.Trim() == "title-type")
                           .SingleOrDefault();

                        return new
                        {
                            name,
                            asFile = asFileElement?.Value.Trim(),
                            titleType = titleTypeElement?.Value.Trim(),
                        };
                    })
                    .ToArray();

                string title;
                string? titleFileAs;
                if (titles.Length <= 0)
                {
                    throw new Exception("\"dc:title\" 要素が見つかりません。");
                }
                else if (titles.Length == 1)
                {
                    (title, titleFileAs) = (titles[0].name, titles[0].asFile);
                }
                else if (titles.Length == 2)
                {
                    var titleType0 = titles[0].titleType;
                    var titleType1 = titles[1].titleType;
                    if (titleType0 is null || titleType1 is null)
                        throw new Exception("\"dc:title\" 要素が複数ありますが、\"title-type\" メタデータが定義されていません。");

                    if (titleType0 == "main" && titleType1 == "subtitle")
                    {
                        // NOP
                    }
                    else if (titleType0 == "subtitle" && titleType1 == "main")
                    {
                        (titles[0], titles[1]) = (titles[1], titles[0]);
                    }
                    else
                    {
                        throw new Exception("\"dc:title\" 要素の \"title-type\" 属性が未知の値です。");
                    }

                    var title0 = titles[0].name;
                    var titleFileAs0 = titles[0].asFile;
                    var title1 = titles[1].name;
                    var titleFileAs1 = titles[1].asFile;

                    title = $"{title0} {title1}";
                    titleFileAs =
                        titleFileAs0 is null
                        ? titleFileAs1
                        : titleFileAs1 is null
                        ? titleFileAs0
                        : $"{titleFileAs0} {titleFileAs1}";
                }
                else
                {
                    throw new Exception("\"dc:title\" 要素が多すぎます。");
                }

                var creators =
                    packageDocument.Descendants(XName.Get("creator", dcNamespace))
                    .Select(creatorElement =>
                    {
                        var name = creatorElement.Value.Trim();
                        var id = creatorElement.Attribute("id")?.Value.Trim();

                        var propertyElements =
                            id is null
                            ? Array.Empty<XElement>()
                            : metadataElements
                                .Where(element => element.Attribute("refines")?.Value.Trim() == $"#{id}")
                                .ToList()
                                .AsEnumerable();

                        var roleElement =
                            propertyElements.
                            Where(element => element.Attribute("property")?.Value.Trim() == "role")
                            .SingleOrDefault();
                        var roleScheme = roleElement?.Attribute("scheme")?.Value.Trim();
                        var fileAsElement =
                            propertyElements.
                            Where(element => element.Attribute("property")?.Value.Trim() == "file-as")
                            .SingleOrDefault();
                        var displaySeqElement =
                            propertyElements.
                            Where(element => element.Attribute("property")?.Value.Trim() == "display-seq")
                            .SingleOrDefault();

                        var displaySeqText = displaySeqElement?.Value.Trim();
                        return new
                        {
                            name,
                            roleScheme,
                            role = roleElement?.Value.Trim(),
                            fileAs = fileAsElement?.Value.Trim(),
                            displaySeq =
                                displaySeqText is not null
                                ? int.Parse(displaySeqText, NumberStyles.None, CultureInfo.InvariantCulture.NumberFormat)
                                : (int?)null,
                        };
                    })
                    .OrderBy(item => item.displaySeq)
                    .ToList();

                var publisherElement =
                    packageDocument.Descendants(XName.Get("publisher", dcNamespace))
                    .SingleOrDefault();
                var publisherFileAsAttribute =
                    metadataElements
                    .Where(element =>
                    {
                        var id = publisherElement?.Attribute("id")?.Value.Trim();
                        return
                            id is not null
                            && element.Attribute("refines")?.Value.Trim() == $"#{id}"
                            && element.Attribute("property")?.Value.Trim() == "file-as";
                    })
                    .SingleOrDefault();

                var languageElement =
                    packageDocument.Descendants(XName.Get("language", dcNamespace))
                    .Single();

                var subjectElement =
                    packageDocument.Descendants(XName.Get("subject", dcNamespace))
                    .SingleOrDefault();

                var descriptionElement =
                    packageDocument.Descendants(XName.Get("description", dcNamespace))
                    .SingleOrDefault();

                var modifiedElement =
                    metadataElements
                    .Where(element => element.Attribute("property")?.Value.Trim() == "dcterms:modified")
                    .SingleOrDefault();

                var modified = modifiedElement is not null ? DateTimeOffset.Parse(modifiedElement.Value.Trim()) : (DateTimeOffset?)null;
                return
                    new EpubPackageDocumentSummary(
                        (title, titleFileAs),
                        creators.Select(item => (item.name, item.fileAs, item.role, item.roleScheme, item.displaySeq)),
                        publisherElement is not null ? (publisherElement.Value.Trim(), publisherFileAsAttribute?.Value.Trim()) : null,
                        languageElement.Value.Trim(),
                        modified,
                        subjectElement is not null ? subjectElement.Value.Trim().Split(',') : Array.Empty<string>(),
                        descriptionElement?.Value.Trim());
            }
            catch (Exception ex)
            {
                throw new Exception("\"META-INF/container.xml\" ファイルの解析に失敗しました。", ex);
            }
        }
    }
}
