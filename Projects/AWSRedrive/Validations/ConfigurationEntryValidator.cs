using AWSRedrive.Models;
using FluentValidation;

namespace AWSRedrive.Validations
{
    public class ConfigurationEntryValidator : AbstractValidator<ConfigurationEntry>
    {
        public ConfigurationEntryValidator()
        {
            RuleFor(x => x.QueueUrl).NotEmpty();
            RuleFor(x => x)
              .Custom((model, context) => {
                int setCount = 0;
                if (!string.IsNullOrEmpty(model.RedriveUrl)) {
                  setCount++;
                }
                if (!string.IsNullOrEmpty(model.RedriveScript)) {
                  setCount++;
                }
                if (!string.IsNullOrEmpty(model.RedriveKafkaTopic)) {
                  setCount++;
                }

                if (setCount > 1) {
                  context.AddFailure("Only one of RedriveUrl, RedriveScript or RedriveKafkaTopic can be specified.");
                } else if (setCount == 0) {
                  context.AddFailure("At least one of RedriveUrl, RedriveScript or RedriveKafkaTopic must be specified.");
                }
              });
            RuleFor(x => x.UseGET).Equal(false).When(x => x.UseDelete || x.UsePUT);
            RuleFor(x => x.UseDelete).Equal(false).When(x => x.UseGET || x.UsePUT);
            RuleFor(x => x.UsePUT).Equal(false).When(x => x.UseGET || x.UseDelete);
        }
    }
}
