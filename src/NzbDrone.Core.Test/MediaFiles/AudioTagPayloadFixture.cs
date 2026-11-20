using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.AutoMoq;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class AudioTagPayloadFixture : CoreTest<AudioTagService>
    {
        [Test]
        public void should_preserve_pcm_samples_when_rewriting_audiobook_tags()
        {
            var path = Path.Combine(TempFolder, "synthetic-chapter.wav");
            WriteSyntheticWave(path);
            new AudioTag { Title = "Before rewrite" }.Write(path);
            var before = File.ReadAllBytes(path);
            var format = ReadWaveChunk(path, "fmt "u8);
            var samples = ReadWaveChunk(path, "data"u8);
            samples.Should().HaveCount(16000);

            Mocker.SetConstant(Mocker.Resolve<IDiskProvider>(FileSystemType.Actual));
            Mocker.GetMock<IConfigService>().SetupGet(config => config.WriteAudioTags).Returns(WriteAudioTagsType.Sync);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.ScrubAudioTags).Returns(true);
            Subject.ReadAudioTag(path).Title.Should().Be("Before rewrite");

            var author = new Author { Name = "Synthetic Author" };
            var book = new Book { Title = "Synthetic Audiobook", Author = author };
            var edition = new Edition { Title = "After rewrite", Book = book };
            var file = new BookFile { Path = path, Author = author, Edition = edition, Part = 1 };

            Subject.WriteTags(file, newDownload: false);

            Subject.ReadAudioTag(path).Title.Should().Be("After rewrite");
            File.ReadAllBytes(path).SequenceEqual(before).Should().BeFalse();
            ReadWaveChunk(path, "fmt "u8).Should().Equal(format);
            ReadWaveChunk(path, "data"u8).Should().Equal(samples);
            VerifyEventPublished<BookFileRetaggedEvent>();
        }

        private static void WriteSyntheticWave(string path)
        {
            var samples = Enumerable.Range(0, 16000).Select(index => (byte)(index % 251)).ToArray();
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write("RIFF"u8);
            writer.Write(36 + samples.Length);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(8000); // sample rate
            writer.Write(16000); // bytes per second
            writer.Write((short)2); // bytes per sample
            writer.Write((short)16); // bits per sample
            writer.Write("data"u8);
            writer.Write(samples.Length);
            writer.Write(samples);
        }

        private static byte[] ReadWaveChunk(string path, ReadOnlySpan<byte> expectedName)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            var stream = reader.BaseStream;
            stream.Position = 12; // Skip RIFF size and WAVE identifiers.
            while (stream.Position + 8 <= stream.Length)
            {
                var name = reader.ReadBytes(4);
                var size = reader.ReadUInt32();
                if (name.AsSpan().SequenceEqual(expectedName))
                {
                    var chunk = reader.ReadBytes(checked((int)size));
                    chunk.LongLength.Should().Be(size);
                    return chunk;
                }

                stream.Seek(size + (size & 1), SeekOrigin.Current);
            }

            throw new AssertionException("Expected WAVE chunk was not found");
        }
    }
}
