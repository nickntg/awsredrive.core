using FluentValidation;

namespace AWSRedrive.Validations
{
    public class ConfigurationEntryValidator : AbstractValidator<ConfigurationEntry>
    {
        public ConfigurationEntryValidator()
        {
            RuleFor(x => x.QueueUrl).NotEmpty();
            RuleFor(x => x.RedriveUrl).NotEmpty().When(x => string.IsNullOrEmpty(x.RedriveScript));
            RuleFor(x => x.RedriveScript).NotEmpty().When(x => string.IsNullOrEmpty(x.RedriveUrl));
        }
    }
}
