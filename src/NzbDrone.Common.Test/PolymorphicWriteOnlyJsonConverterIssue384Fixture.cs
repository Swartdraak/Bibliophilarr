using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test
{
    // Regression tests for issue #384: PolymorphicWriteOnlyJsonConverter.Read
    // infinite recursion (StackOverflowException) when a command error is
    // serialised for the response/log.
    [TestFixture]
    public class PolymorphicWriteOnlyJsonConverterIssue384Fixture : TestBase
    {
        // Test-only concrete type used to exercise the converter directly.
        [JsonConverter(typeof(PolymorphicWriteOnlyJsonConverter<TestCommand>))]
        private abstract class TestCommand
        {
            public string Name { get; set; }
            public int Priority { get; set; }
        }

        private class ConcreteTestCommand : TestCommand
        {
            public ConcreteTestCommand()
            {
                Name = "ConcreteTest";
                Priority = 5;
            }

            public string ExtraField { get; set; } = "hello";
        }

        [Test]
        public void write_should_serialize_concrete_type_properties()
        {
            var cmd = new ConcreteTestCommand();
            var options = new JsonSerializerOptions();
            options.Converters.Add(new PolymorphicWriteOnlyJsonConverter<TestCommand>());

            var json = JsonSerializer.Serialize((TestCommand)cmd, options);

            json.Should().Contain("\"Name\":\"ConcreteTest\"");
            json.Should().Contain("\"Priority\":5");
            json.Should().Contain("\"ExtraField\":\"hello\"");
        }

        [Test]
        public void write_should_use_runtime_type_not_static_type()
        {
            var cmd = new ConcreteTestCommand { ExtraField = "polymorphic" };
            var options = new JsonSerializerOptions();
            options.Converters.Add(new PolymorphicWriteOnlyJsonConverter<TestCommand>());

            // Cast to static type; Write must still emit the runtime type's fields.
            TestCommand staticRef = cmd;
            var json = JsonSerializer.Serialize(staticRef, options);

            json.Should().Contain("polymorphic");
        }

        [Test]
        public void read_with_converter_in_options_should_not_recurse_infinite()
        {
            // This is the exact call pattern that triggered the StackOverflow:
            //   JsonSerializer.Deserialize<TestCommand>(json, options)
            // where options contains the PolymorphicWriteOnlyJsonConverter<TestCommand>.
            // The converter's Read is invoked by the serializer, and the original
            // implementation called Deserialize again with the SAME options,
            // re-entering Read indefinitely.
            //
            // The fix: a thread-local recursion guard.  On the outermost call
            // (_readDepth == 0) Read proceeds with a Deserialize that throws a
            // catchable JsonException for the abstract type.  On the re-entrant
            // call (_readDepth >= 1) Read returns default immediately, breaking
            // the cycle.  If the bug regresses, the test process crashes with a
            // StackOverflowException (not catchable) and the runner reports a
            // failure.
            var json = "{\"name\":\"Test\",\"priority\":1}";
            var options = new JsonSerializerOptions();
            options.Converters.Add(new PolymorphicWriteOnlyJsonConverter<TestCommand>());

            // Must throw a catchable exception (NOT StackOverflowException).
            // For the abstract TestCommand the outermost Read's Deserialize throws
            // JsonException; the guard ensures no infinite recursion.
            var act = () => JsonSerializer.Deserialize<TestCommand>(json, options);

            act.Should().Throw<JsonException>();
        }

        [Test]
        public void read_should_not_mutate_options_state()
        {
            // Read must not mutate the options or the converter instance.
            var json = "{\"name\":\"Test\",\"priority\":1}";
            var options = new JsonSerializerOptions();
            var converter = new PolymorphicWriteOnlyJsonConverter<TestCommand>();
            options.Converters.Add(converter);

            var converterCountBefore = options.Converters.Count;

            var act = () => JsonSerializer.Deserialize<TestCommand>(json, options);
            act.Should().Throw<JsonException>();

            // Options should be unchanged.
            options.Converters.Count.Should().Be(converterCountBefore);
            options.Converters.Should().Contain(converter);
        }

        [Test]
        public void command_error_serialization_should_not_crash_application()
        {
            // This test mirrors the production scenario from issue #384:
            //   1. A command (e.g. RescanFolders) fails.
            //   2. The command error is serialised for the response/log.
            //   3. The serialization path must NOT crash the application.
            //
            // We use the REAL Command class (from NzbDrone.Core) which has
            // [JsonConverter(typeof(PolymorphicWriteOnlyJsonConverter<Command>))].
            //
            // Write path: serialize a concrete command → must succeed.
            var rescanCmd = new NzbDrone.Core.MediaFiles.Commands.RescanFoldersCommand();
            var json = STJson.ToJson(rescanCmd);
            json.Should().NotBeNullOrEmpty();
            json.Should().Contain("RescanFolders");

            // Read path: deserialize the command JSON → the converter's Read is invoked.
            // The original code recursed infinitely → StackOverflowException → crash.
            // The fixed code: thread-local guard breaks the recursion; the outermost
            // Read's Deserialize throws a catchable JsonException (abstract Command
            // cannot be constructed) instead of crashing the process.
            var act = () => STJson.Deserialize(json, typeof(NzbDrone.Core.Messaging.Commands.Command));

            // Must throw a catchable exception, not crash the process.
            act.Should().Throw<JsonException>();
        }

        [Test]
        public void write_only_converter_read_should_be_idempotent_safe()
        {
            // Calling Read twice with the same options must not cause any issue
            // (no state mutation, no accumulation, no leaked recursion depth).
            var json = "{\"name\":\"Test\",\"priority\":1}";
            var options = new JsonSerializerOptions();
            var converter = new PolymorphicWriteOnlyJsonConverter<TestCommand>();
            options.Converters.Add(converter);

            // First call — should throw catchable JsonException (abstract type).
            var act1 = () => JsonSerializer.Deserialize<TestCommand>(json, options);
            act1.Should().Throw<JsonException>();

            // Second call — same options, same converter instance; must behave
            // identically (the thread-local _readDepth is reset to 0 after each
            // top-level call by the finally block).
            var act2 = () => JsonSerializer.Deserialize<TestCommand>(json, options);
            act2.Should().Throw<JsonException>();

            // Options should still contain the converter (Read must not mutate it).
            options.Converters.Should().Contain(converter);
        }
    }
}
