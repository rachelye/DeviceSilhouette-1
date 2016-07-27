﻿using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using CommunicationProviders.IoTHub;
using Microsoft.Azure.Devices;
using DeviceRepository.Interfaces;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using DeviceRichState;

namespace IoTHubFeedbackService
{
    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class IoTHubFeedbackService : StatelessService
    {
        private static Uri RepositoryUri = new Uri("fabric:/StateManagementService/DeviceRepositoryActorService");
        private IoTHubFeedbackProcessor _feedbackProcessor;

        public IoTHubFeedbackService(StatelessServiceContext context,
            string iotHubConnectionString)
            : base(context)
        {
            _feedbackProcessor = new IoTHubFeedbackProcessor(iotHubConnectionString, ProcessFeedbackAsync);
        }

        private async Task ProcessFeedbackAsync(FeedbackRecord feedbackRecord)           
        {
            // TODO - handle actor not found for deviceID exception
            IDeviceRepositoryActor silhouette = GetDeviceActor(feedbackRecord.DeviceId);
            DeviceMessage state = new DeviceMessage(
                feedbackRecord.DeviceId,
                "",
                "",
                MessageType.CommandResponse,
                GetMessageSubTypeFromFeedbck(feedbackRecord.StatusCode),
                -1,
                feedbackRecord.OriginalMessageId,
                feedbackRecord.EnqueuedTimeUtc
                );
            await silhouette.StoreDeviceMessageAsync(state);
        }

        private MessageSubType GetMessageSubTypeFromFeedbck(FeedbackStatusCode status)
        {
            switch (status) {
                case FeedbackStatusCode.Success:
                    return MessageSubType.Acknowledged;
                case FeedbackStatusCode.Rejected:
                    return MessageSubType.NotAcknowledged;
                case FeedbackStatusCode.Expired:
                    return MessageSubType.Expired;
                case FeedbackStatusCode.DeliveryCountExceeded:
                    return MessageSubType.ExceededRetryCount;
            }
            return MessageSubType.Unknown;
        }

        private static IDeviceRepositoryActor GetDeviceActor(string deviceId)
        {
            ActorId actorId = new ActorId(deviceId);
            IDeviceRepositoryActor silhouette = ActorProxy.Create<IDeviceRepositoryActor>(actorId, RepositoryUri);
            return silhouette;
        }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[0];
        }

        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {                                
            await _feedbackProcessor.ReceviceFeedback(cancellationToken);                           
        }
    }
}
