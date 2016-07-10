﻿using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using CommunicationProviders.IoTHub;
using CommunicationProviders;
using System.Configuration;
using DeviceRichState;
using DeviceRepository.Interfaces;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using Newtonsoft.Json;
using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;

namespace CommunicationProviderService
{
    public interface ICommunicationProviderRemoting : IService
    {
        Task InvokeDeepReadStateAsync(string deviceId, double timeToLive);
        Task SendCloudToDeviceAsync(DeviceState deviceState, string messageType, double timeToLive);
    }

    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class CommunicationProviderService : StatelessService, ICommunicationProviderRemoting
    {
        private static Uri RepositoryUri = new Uri("fabric:/StateManagementService/DeviceRepositoryActorService");
        private readonly JsonSerializer _jsonSerializer;
        private readonly IMessageReceiver _messageReceiver;
        private readonly IotHubMessageSender _messageSender;

        public CommunicationProviderService(StatelessServiceContext context)
            : base(context)
        {
            // init communicaition provider for Azure IoTHub
            string iotHubConnectionString = ConfigurationManager.AppSettings["iotHubConnectionString"];
            string storageConnectionString = ConfigurationManager.AppSettings["storageConnectionString"];
            _messageReceiver = new IoTHubMessageReceiver(
                iotHubConnectionString, 
                storageConnectionString,
                ProcessCommunicationProviderMessageAsync
                );

            _messageSender = new IotHubMessageSender(iotHubConnectionString);

            _jsonSerializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                CheckAdditionalContent = true,
            });
        }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[] { new ServiceInstanceListener(context =>
            this.CreateServiceRemotingListener(context)) };
        }

        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following sample code with your own logic 
            //       or remove this RunAsync override if it's not needed in your service.
            await _messageReceiver.RunAsync(cancellationToken);
        }

        // process messages from communication provider - D2C endpoint 
        private async Task ProcessCommunicationProviderMessageAsync(string message)
        {
            if (!String.IsNullOrEmpty(message))
            {
                try
                {
                    JsonState jsonState = _jsonSerializer.Deserialize<JsonState>(message);

                    // TODO - add assert if device id exist. Create if not?
                    switch (jsonState.SilhouetteProperties.Status)
                    {
                        case "Reported": // device reporting a state update                            
                            await UpdateDeviceSilhouetteAsync(jsonState);
                            break;
                        case "Get": // device requesting last stored state
                            await UpdateDeviceStateAsync(jsonState);
                            break;
                    }
                }
                catch (Exception)
                {
                    // TODO: better error handling                    
                }
            }
        }

        // send a message to the device using the C2D endpoint. called when a device is requesting the last stored state
        private async Task UpdateDeviceStateAsync(JsonState jsonState)
        {
            try { 
                IDeviceRepositoryActor silhouette = GetDeviceActor(jsonState.SilhouetteProperties.DeviceId);
                DeviceState deviceState = await silhouette.GetDeviceStateAsync();            
                await _messageSender.SendCloudToDeviceAsync(deviceState.Values, "State:Get", deviceState.DeviceId, jsonState.SilhouetteProperties.MessageTTL, deviceState.CorrelationId);
            }
            catch (Exception e)
            {
                // TODO: handle cases when the device is not found in the state repository
            }
        }

        // StateMessage example: {"DeviceID":"silhouette1","Timestamp":1464524365618,"Status":"Reported","State":{"Xaxis":"0","Yaxis":"0","Zaxis":"0"}}
        // update the device state in the repository - the device is reporting a state update  
        private async Task<DeviceState> UpdateDeviceSilhouetteAsync(JsonState jsonState)
        {
            //TODO: error handling
            var deviceId = jsonState.SilhouetteProperties.DeviceId;
            Types messageType = Enum.TryParse<Types>(jsonState.SilhouetteProperties.Status, true, out messageType) ? messageType : Types.Report;
            IDeviceRepositoryActor silhouette = GetDeviceActor(deviceId);

            DeviceState deviceState = new DeviceState(deviceId, jsonState.AppMetadata.ToString(), jsonState.DeviceValues.ToString(), messageType)
            {                   
                Timestamp = jsonState.SilhouetteProperties.Timestamp
            };

            // update device repository
            return await silhouette.SetDeviceStateAsync(deviceState);
        }

        private static IDeviceRepositoryActor GetDeviceActor(string deviceId)
        {
            ActorId actorId = new ActorId(deviceId);
            IDeviceRepositoryActor silhouette = ActorProxy.Create<IDeviceRepositoryActor>(actorId, RepositoryUri);
            return silhouette;
        }

        // Send a get request directly from the device, not going through the device repository
        // The device with send the state in a sepetate call
        // message example:
        //{
        //"DeviceID" : "Device1",
        //"Timestamp" : "2009-06-15T13:45:30",
        //"Status" : "GetInfo" 
        //}
        public async Task InvokeDeepReadStateAsync(string deviceId, double timeToLive)
        {
            JsonState state = new JsonState();
            state.SilhouetteProperties.DeviceId = deviceId;
            state.SilhouetteProperties.Timestamp = DateTime.Now;
            state.SilhouetteProperties.Status = "GetInfo";
            string message = _jsonSerializer.Serialize(state);           

            // update the state repository with the new message
            IDeviceRepositoryActor silhouette = GetDeviceActor(deviceId);
            DeviceState deviceState = new DeviceState(deviceId, "", "", Types.Report, Status.Enqueued);

            // update C2D end point with the request to state update
            await _messageSender.SendCloudToDeviceAsync(deviceId, "State:Get", message, timeToLive, deviceState.CorrelationId);            
        }

        public async Task SendCloudToDeviceAsync(DeviceState deviceState, string messageType, double timeToLive)
        {
            // update device with the new state (C2D endpoint)
            string json = _jsonSerializer.Serialize(deviceState);
            //await _messageSender.SendCloudToDeviceAsync(deviceState.DeviceId, messageType, json, timeToLive, deviceState.CorrelationId);
            await _messageSender.SendCloudToDeviceAsync(deviceState.DeviceId, messageType, deviceState.Values, timeToLive, deviceState.CorrelationId);
        }

        private class JsonState
        {

            public JsonState()
            {
                SilhouetteProperties = new Properties();
            }
            public Properties SilhouetteProperties { get; set; }
            public Object AppMetadata { get; set; }
            public Object DeviceValues { get; set; }

            public class Properties
            {
                public string DeviceId { get; set; }
                public DateTime Timestamp { get; set; }
                public int Version { get; set; }
                public string Status { get; set; }
                public double MessageTTL {get; set; }
            }


        }
    }
}