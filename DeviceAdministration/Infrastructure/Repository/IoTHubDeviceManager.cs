﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.Common.Configurations;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Infrastructure.Models;

namespace Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Infrastructure.Repository
{
    /// <summary>
    ///     Wraps calls to the IoT hub identity store.
    ///     IDisposable is implemented in order to close out the connection to the IoT Hub when this object is no longer in use
    /// </summary>
    public class IoTHubDeviceManager : IIoTHubDeviceManager, IDisposable
    {
        private readonly RegistryManager _deviceManager;
        private readonly ServiceClient _serviceClient;
        private readonly JobClient _jobClient;
        private bool _disposed;

        public IoTHubDeviceManager(IConfigurationProvider configProvider)
        {
            // Temporary code to bypass https cert validation till DNS on IotHub is configured
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
            var iotHubConnectionString = configProvider.GetConfigurationSettingValue("iotHub.ConnectionString");
            this._deviceManager = RegistryManager.CreateFromConnectionString(iotHubConnectionString);
            this._serviceClient = ServiceClient.CreateFromConnectionString(iotHubConnectionString);
            this._jobClient = JobClient.CreateFromConnectionString(iotHubConnectionString);
        }

        public async Task<Device> AddDeviceAsync(Device device)
        {
            return await this._deviceManager.AddDeviceAsync(device);
        }

        public async Task<Device> GetDeviceAsync(string deviceId)
        {
            return await this._deviceManager.GetDeviceAsync(deviceId);
        }

        public async Task RemoveDeviceAsync(string deviceId)
        {
            await this._deviceManager.RemoveDeviceAsync(deviceId);
        }

        public async Task<Device> UpdateDeviceAsync(Device device)
        {
            return await this._deviceManager.UpdateDeviceAsync(device);
        }

        public async Task SendAsync(string deviceId, Message message)
        {
            await this._serviceClient.SendAsync(deviceId, message);
        }

        public async Task<CloudToDeviceMethodResult> InvokeDeviceMethodAsync(string deviceId, CloudToDeviceMethod method)
        {
            return await this._serviceClient.InvokeDeviceMethodAsync(deviceId, method);
        }

        public async Task CloseAsyncDevice()
        {
            await this._serviceClient.CloseAsync();
        }

        public async Task CloseAsyncService()
        {
            await this._deviceManager.CloseAsync();
        }

        public async Task<Twin> GetTwinAsync(string deviceId)
        {
            return await _deviceManager.GetTwinAsync(deviceId);
        }

        public async Task UpdateTwinAsync(string deviceId, Twin twin)
        {
            await this._deviceManager.UpdateTwinAsync(deviceId, twin, twin.ETag);
        }

        public async Task ScheduleTwinUpdate(string deviceId,Twin twin, DeviceListQuery query,DateTime startDateUtc, long maxExecutionTimeInSeconds)
        {
            var sqlQuery = query.GetSQLQuery();
            await this._jobClient.ScheduleTwinUpdateAsync(Guid.NewGuid().ToString(), sqlQuery, twin, startDateUtc, maxExecutionTimeInSeconds);
        }

        public async Task<IEnumerable<Twin>> QueryDevicesAsync(DeviceListQuery query)
        {
            var sqlQuery = query.GetSQLQuery();
            var deviceQuery = this._deviceManager.CreateQuery(sqlQuery);

            var twins = new List<Twin>();
            while (deviceQuery.HasMoreResults)
            {
                twins.AddRange(await deviceQuery.GetNextAsTwinAsync());
            }

            return twins;
        }

        public async Task<long> GetDeviceCountAsync()
        {
            return (await this._deviceManager.GetRegistryStatisticsAsync()).TotalDeviceCount;
        }

        public async Task<IEnumerable<DeviceJob>> GetDeviceJobsByDeviceIdAsync(string deviceId)
        {
            return await this.QueryDeviceJobs($"SELECT * FROM devices.jobs WHERE devices.jobs.deviceId='{deviceId}'");
        }

        public async Task<IEnumerable<DeviceJob>> GetDeviceJobsByJobIdAsync(string jobId)
        {
            return await this.QueryDeviceJobs($"SELECT * FROM devices.jobs WHERE devices.jobs.jobId='{jobId}'");
        }

        public async Task<IEnumerable<string>> GetJobResponsesAsync()
        {
            var jobQuery = _jobClient.CreateQuery();

            var results = new List<string>();
            while (jobQuery.HasMoreResults)
            {
                results.AddRange(await jobQuery.GetNextAsJsonAsync());
            }

            return results;
        }

        public async Task<JobResponse> GetJobResponseByJobIdAsync(string jobId)
        {
            return await this._jobClient.GetJobAsync(jobId);
        }

        public async Task<IEnumerable<JobResponse>> GetJobResponsesByStatus(JobStatus status)
        {
            JobStatus? queryStatus = status;

            // [WORDAROUND] 'Scheduled' is not available for query. Query all jobs then filter at application level as workaround
            if (status == JobStatus.Scheduled)
            {
                queryStatus = null;
            }

            var jobs = new List<JobResponse>();

            var query = this._jobClient.CreateQuery(null, queryStatus);
            while (query.HasMoreResults)
            {
                var result = await query.GetNextAsJobResponseAsync();
                jobs.AddRange(result.Where(j => j.Status == status));
            }

            return jobs;
        }

        public async Task<JobResponse> CancelJobByJobIdAsync(string jobId)
        {
            return await this._jobClient.CancelJobAsync(jobId);
        }

        private async Task<IEnumerable<DeviceJob>> QueryDeviceJobs(string sqlQueryString)
        {
            var jobQuery = this._deviceManager.CreateQuery(sqlQueryString);

            var results = new List<DeviceJob>();
            while (jobQuery.HasMoreResults)
            {
                results.AddRange(await jobQuery.GetNextAsDeviceJobAsync());
            }

            return results;
        }

        #region IDispose
        /// <summary>
        ///     Implement the IDisposable interface in order to close the device manager
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this._disposed)
            {
                return;
            }

            if (disposing)
            {
                if (this._deviceManager != null)
                {
                    this._deviceManager.CloseAsync().Wait();
                }
            }

            this._disposed = true;
        }

        ~IoTHubDeviceManager()
        {
            this.Dispose(false);
        }
        #endregion
    }
}
