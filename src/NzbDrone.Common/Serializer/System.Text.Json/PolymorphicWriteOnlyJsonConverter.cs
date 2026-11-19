using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Common.Serializer
{
    /// <summary>
    /// A JSON converter that serialises the type parameter polymorphically (using the
    /// runtime type of the value) but does NOT support deserialisation.
    /// The class is "write-only" in the sense that deserialising an instance is not
    /// meaningful: the concrete subtype cannot be determined from the JSON payload
    /// alone, and constructing an instance from arbitrary JSON could produce an
    /// object in an invalid state.
    /// Recursion guard (issue #384): the original implementation of Read called
    /// JsonSerializer.Deserialize with the same options that registered this
    /// converter (via the JsonConverter attribute on the type).  System.Text.Json
    /// therefore re-entered the Read method on every call, producing unbounded
    /// recursion and a StackOverflowException that crashed the entire application
    /// whenever a command error (or any command payload) was serialised for the
    /// response/log.
    /// </summary>
    public class PolymorphicWriteOnlyJsonConverter<T> : JsonConverter<T>
    {
        // Thread-local recursion guard (issue #384).
        //
        // System.Text.Json resolves [JsonConverter] attributes via the type's
        // serialization metadata cache, NOT via the options.Converters collection.
        // Therefore removing this converter from a copy of options does NOT prevent
        // re-entry: the inner Deserialize still finds the converter on the type and
        // calls Read again, recursing until the stack overflows.
        //
        // The guard: a per-thread counter.  On the FIRST (outermost) call the
        // counter is 0 — proceed with a safe deserialise (which throws a catchable
        // JsonException for the abstract Command base type).  On any RE-ENTRANT
        // call the counter is >= 1 — return null immediately, breaking the cycle.
        //
        // [ThreadStatic] ensures per-thread state without a lock; a static field
        // with a lock would add contention on a hot serialization path.
        [ThreadStatic]
        private static int _readDepth;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Re-entrant call: the outer Read already triggered a Deserialize that
            // found this converter on the type.  Breaking the cycle here prevents
            // the infinite recursion that caused the StackOverflowException in
            // issue #384.  The outer call receives a null (or the abstract-type
            // JsonException) and the application survives.
            if (_readDepth > 0)
            {
                return default;
            }

            _readDepth++;
            try
            {
                // Deserialise with the SAME options (all property-level converters
                // such as JsonStringEnumConverter, STJHttpUriConverter, etc. are
                // preserved).  For the abstract Command base type this throws a
                // catchable JsonException ("cannot construct abstract type") —
                // the application logs it as a normal error instead of crashing.
                return JsonSerializer.Deserialize<T>(ref reader, options);
            }
            finally
            {
                _readDepth--;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
