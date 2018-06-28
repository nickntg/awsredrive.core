using FluentValidation;

namespace AWSRedrive.Validations
{
    public class ConfigurationEntryValidator : AbstractValidator<ConfigurationEntry>
    {
        public ConfigurationEntryValidator()
        {
            RuleFor(x => x.QueueUrl).NotEmpty();
            RuleFor(x => x.RedriveUrl).NotEmpty();
        }
    }
}
