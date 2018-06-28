using System.Collections.Generic;

namespace AWSRedrive.Interfaces
{
    public interface IConfigurationChangeManager
    {
        void ReadChanges(IConfigurationReader configurationReader,
            List<IQueueProcessor> processors,
            IQueueClientFactory queueClientFactory,
            IMessageProcessorFactory messageProcessorFactory,
            IQueueProcessorFactory queueProcessorFactory);
    }
}
