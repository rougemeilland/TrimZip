using System;
using System.Collections.Generic;
using System.Linq;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Console;

namespace TrimZip.CUI
{
    internal class Program
    {
        private static readonly string _thisApplicationName;

        static Program()
        {
            _thisApplicationName = typeof(Program).Assembly.GetAssemblyFileNameWithoutExtension();       
        }

        static int Main(string[] args)
        {
            try
            {
                var zipFiles =
                    args.EnumerateFilesFromArgument(true)
                        .Where(file => file.Extension.IsAnyOf(".zip", ".epub", StringComparison.OrdinalIgnoreCase) && CheckIfValidFilePath(file))
                        .ToList();
                var processedCount = 0;
                foreach (var zipFile in zipFiles)
                {
                    TinyConsole.Erase(ConsoleEraseMode.FromCursorToEndOfLine);
                    TinyConsole.Write($"{(double)processedCount / zipFiles.Count * 100:F2}%: processing \"{zipFile.Name}\"");
                    TinyConsole.Write("\r");
                    try
                    {
                        var arrayOfLength = EnumerateLengthOfZipFile(zipFile).ToArray();
                        if (arrayOfLength.Length <= 0)
                            throw new Exception($"It is not a ZIP format file.: \"{zipFile.FullName}\"");
                        var actualLength = arrayOfLength[0];
                        if (zipFile.Length > actualLength)
                        {
                            using var outStream = zipFile.OpenWrite();
                            outStream.Length = actualLength;
                            TinyConsole.Erase(ConsoleEraseMode.FromCursorToEndOfLine);
                            TinyConsole.WriteLine($"Trimmed: \"{zipFile.Name}\"");
                        }
                    }
                    catch (Exception ex)
                    {
                        TinyConsole.Erase(ConsoleEraseMode.FromCursorToEndOfLine);
                        PrintException(ex);
                    }

                    ++processedCount;
                }
            }
            catch (Exception ex)
            {
                PrintException(ex);
                TinyConsole.Beep();
                _ = TinyConsole.ReadLine();
                return 1;
            }

            TinyConsole.Erase(ConsoleEraseMode.FromCursorToEndOfLine);
            TinyConsole.WriteLine("Completed.");
            TinyConsole.Beep();
            _ = TinyConsole.ReadLine();

            return 0;
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
            while ( count < MAXIMUM_EOCDR_LENGTH && enumerator.MoveNext())
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

        private static void PrintErrorMessage(string message, int indent = 0)
        {
            TinyConsole.ForegroundColor = ConsoleColor.Red;
            try
            {
                TinyConsole.WriteLine($"{_thisApplicationName}: {new string(' ', indent * 2)}{message}");
            }
            finally
            {
                TinyConsole.ResetColor();
            }
        }

        private static void PrintException(Exception exception, int indent = 0)
        {
            PrintErrorMessage(exception.Message, indent);
            if (exception.InnerException is not null)
               PrintException(exception.InnerException, indent + 1);
            if (exception is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                    PrintException(innerException, indent + 1);
            }
        }
    }
}
