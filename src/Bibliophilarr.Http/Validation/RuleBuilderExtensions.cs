using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Validators;

namespace Bibliophilarr.Http.Validation
{
    public static class RuleBuilderExtensions
    {
        public static IRuleBuilderOptions<T, int> ValidId<T>(this IRuleBuilder<T, int> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new GreaterThanValidator(0));
        }

        public static IRuleBuilderOptions<T, int> IsZero<T>(this IRuleBuilder<T, int> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new EqualValidator(0));
        }

        public static IRuleBuilderOptions<T, string> HaveHttpProtocol<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new RegularExpressionValidator("^http(s)?://", RegexOptions.IgnoreCase)).WithMessage("must start with http:// or https://");
        }

        public static IRuleBuilderOptions<T, string> NotBlank<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new NotNullValidator()).SetValidator(new NotEmptyValidator(""));
        }

        public static IRuleBuilderOptions<T, IEnumerable<TProp>> EmptyCollection<T, TProp>(this IRuleBuilder<T, IEnumerable<TProp>> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new EmptyCollectionValidator<TProp>());
        }

        public static IRuleBuilderOptions<T, int> IsValidRssSyncInterval<T>(this IRuleBuilder<T, int> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new RssSyncIntervalValidator());
        }

        /// <summary>
        /// Validates that an integer-typed enum property only carries a value that is a
        /// defined member of the enum. Out-of-range values (e.g. 3 for a 0/1/2 enum) are
        /// rejected with a 400 by the REST error pipeline, preventing configuration
        /// corruption from being persisted through the API (issue #256).
        /// </summary>
        public static IRuleBuilderOptions<T, TProp> IsInEnum<T, TProp>(this IRuleBuilderInitial<T, TProp> ruleBuilder, Type enumType)
            where TProp : struct
        {
            return ruleBuilder.SetValidator(new InEnumValidator(enumType));
        }
    }

    /// <summary>
    /// FluentValidation property validator that rejects an integer-typed enum property whose
    /// stored value is not a defined member of the target enum (issue #256). Follows the same
    /// PropertyValidator pattern as the repo's RssSyncIntervalValidator.
    /// </summary>
    public class InEnumValidator : PropertyValidator
    {
        private readonly Type _enumType;
        private readonly HashSet<int> _allowed;

        public InEnumValidator(Type enumType)
        {
            _enumType = enumType;
            _allowed = Enum.GetNames(enumType)
                .Select(n => Convert.ToInt32(Enum.Parse(enumType, n)))
                .ToHashSet();
        }

        protected override string GetDefaultMessageTemplate() =>
            $"must be a defined {_enumType.Name} value ({string.Join(", ", _allowed.OrderBy(v => v))})";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            // PropertyValue is boxed: either the enum type (strongly-typed resource property)
            // or an int. Convert.ToInt32 on a boxed enum yields its underlying value, not its
            // ordinal, so the comparison against the set of defined underlying values is correct.
            return _allowed.Contains(Convert.ToInt32(context.PropertyValue));
        }
    }
}
