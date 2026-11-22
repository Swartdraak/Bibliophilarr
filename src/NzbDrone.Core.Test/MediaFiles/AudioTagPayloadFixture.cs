using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
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
            var pcmData = ReadWaveChunk(path, "data"u8);
            pcmData.Should().HaveCount(16000);

            RewriteTitle(path);

            File.ReadAllBytes(path).SequenceEqual(before).Should().BeFalse();
            ReadWaveChunk(path, "fmt "u8).Should().Equal(format);
            ReadWaveChunk(path, "data"u8).Should().Equal(pcmData);
        }

        [Test]
        public void should_preserve_aac_payload_when_rewriting_m4b_tags()
        {
            var path = Path.Combine(TempFolder, "compressed-chapter.m4b");
            var fixture = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "nin.m4a");
            File.Copy(fixture, path);
            new AudioTag { Title = "Before rewrite" }.Write(path);
            var before = File.ReadAllBytes(path);
            var aacData = ReadMpeg4MediaData(path);
            aacData.Length.Should().BeGreaterThan(0);
            TimeSpan duration;
            using (var original = TagLib.File.Create(path))
            {
                original.Properties.AudioSampleRate.Should().Be(44100);
                original.Properties.AudioChannels.Should().Be(2);
                duration = original.Properties.Duration;
            }

            RewriteTitle(path);

            File.ReadAllBytes(path).SequenceEqual(before).Should().BeFalse();
            ReadMpeg4MediaData(path).Should().Equal(aacData);
            using var rewritten = TagLib.File.Create(path);
            rewritten.Properties.AudioSampleRate.Should().Be(44100);
            rewritten.Properties.AudioChannels.Should().Be(2);
            rewritten.Properties.Duration.Should().Be(duration);
        }

        private void RewriteTitle(string path)
        {
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
            VerifyEventPublished<BookFileRetaggedEvent>();
        }

        private static byte[] ReadMpeg4MediaData(string path)
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            while (stream.Length - stream.Position >= header.Length)
            {
                stream.ReadExactly(header);
                var boxSize = BinaryPrimitives.ReadInt32BigEndian(header);
                var payloadSize = boxSize - header.Length;
                ((long)payloadSize).Should().BeInRange(0, stream.Length - stream.Position);
                if (header[4..].SequenceEqual("mdat"u8))
                {
                    var payload = new byte[payloadSize];
                    stream.ReadExactly(payload);
                    return payload;
                }

                stream.Seek(payloadSize, SeekOrigin.Current);
            }

            throw new AssertionException($"Expected MPEG-4 mdat box in {path}");
        }

        private static void WriteSyntheticWave(string path)
        {
            const int sampleRate = 8000;
            const int sampleCount = sampleRate;
            const int dataSize = sampleCount * sizeof(short);
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write("RIFF"u8);
            writer.Write(36 + dataSize);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short)); // bytes per second
            writer.Write((short)sizeof(short)); // bytes per sample
            writer.Write((short)16); // bits per sample
            writer.Write("data"u8);
            writer.Write(dataSize);
            for (var index = 0; index < sampleCount; index++)
            {
                writer.Write((short)(((index * 31) % 10000) - 5000));
            }
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

            throw new AssertionException($"Expected WAVE chunk '{Encoding.ASCII.GetString(expectedName)}' in {path}");
        }
    }
}
