using FluentValidation;

namespace AWSRedrive.Validations
{
    public class ConfigurationEntryValidator : AbstractValidator<ConfigurationEntry>
    {
        public ConfigurationEntryValidator()
        {
            RuleFor(x => x.QueueUrl).NotEmpty();
            RuleFor(x => x.RedriveUrl).NotEmpty().When(x => string.IsNullOrEmpty(x.RedriveScript) && string.IsNullOrEmpty(x.RedriveKafkaTopic));
            RuleFor(x => x.RedriveScript).NotEmpty().When(x => string.IsNullOrEmpty(x.RedriveUrl) && !string.IsNullOrEmpty(x.RedriveKafkaTopic));
            RuleFor(x => x.RedriveKafkaTopic).NotEmpty().When(x => string.IsNullOrEmpty(x.RedriveUrl) && !string.IsNullOrEmpty(x.RedriveScript));
            RuleFor(x => x.UseGET).Equal(false).When(x => x.UseDelete || x.UsePUT);
            RuleFor(x => x.UseDelete).Equal(false).When(x => x.UseGET || x.UsePUT);
            RuleFor(x => x.UsePUT).Equal(false).When(x => x.UseGET || x.UseDelete);
        }
    }
}
