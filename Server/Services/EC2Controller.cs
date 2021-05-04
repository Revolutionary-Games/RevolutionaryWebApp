namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.EC2;
    using Amazon.EC2.Model;
    using Amazon.Runtime;
    using Microsoft.Extensions.Configuration;
    using Shared.Models;

    public class EC2Controller
    {
        private readonly string imageId;
        private readonly string serverKeyId;
        private readonly InstanceType instanceType;
        private readonly string subnet;
        private readonly string securityGroup;
        private readonly string rootFileSystemSnap;
        private readonly string rootFileSystemPath;
        private readonly int defaultVolumeSize;

        private readonly AmazonEC2Client ec2Client;

        public EC2Controller(IConfiguration configuration)
        {
            var region = configuration["CI:AWSRegion"];
            var accessKeyId = configuration["CI:AWSAccessKey"];
            var secretAccessKey = configuration["CI:AWSSecretKey"];

            imageId = configuration["CI:DefaultAMI"];
            serverKeyId = configuration["CI:SSHKeyPair"];
            instanceType = InstanceType.FindValue(configuration["CI:InstanceType"]);
            subnet = configuration["CI:AWSSubnet"];
            securityGroup = configuration["CI:AWSSecurityGroup"];
            rootFileSystemSnap = configuration["CI:RootFileSystemSnap"];
            rootFileSystemPath = configuration["CI:RootFileSystemPath"];
            defaultVolumeSize = Convert.ToInt32(configuration["CI:DefaultVolumeSizeGiB"]);

            // TODO: should *all* the variables be checked here
            if (string.IsNullOrEmpty(region) || string.IsNullOrEmpty(accessKeyId) ||
                string.IsNullOrEmpty(secretAccessKey))
            {
                Configured = false;
                return;
            }

            // A quick sanity check on the volume sizes
            if (defaultVolumeSize < 5 || defaultVolumeSize > 1000)
                throw new ArgumentException("Volume size should be between 5 and 1000 gigabytes");

            ec2Client = new AmazonEC2Client(new BasicAWSCredentials(accessKeyId, secretAccessKey), new AmazonEC2Config()
            {
                AuthenticationRegion = region
            });
        }

        public bool Configured { get; private set; }

        public static ServerStatus InstanceStateToStatus(Instance instance)
        {
            if ((instance.State.Code & 32) != 0)
                return ServerStatus.Stopping;

            if ((instance.State.Code & 48) != 0)
                return ServerStatus.Terminated;

            if ((instance.State.Code & 64) != 0)
                return ServerStatus.Stopping;

            if ((instance.State.Code & 80) != 0)
                return ServerStatus.Stopped;

            if ((instance.State.Code & 16) != 0)
                return ServerStatus.Running;

            // Pending status if no other flag is set
            return ServerStatus.WaitingForStartup;
        }

        public async Task<List<string>> LaunchNewInstance()
        {
            ThrowIfNotConfigured();

            var response = await ec2Client.RunInstancesAsync(new RunInstancesRequest()
            {
                ImageId = imageId,
                KeyName = serverKeyId,
                InstanceType = instanceType,
                SubnetId = subnet,
                SecurityGroupIds = new List<string>() { securityGroup },
                EbsOptimized = true,
                HibernationOptions = new HibernationOptionsRequest()
                {
                    Configured = true
                },
                BlockDeviceMappings = new List<BlockDeviceMapping>()
                {
                    new BlockDeviceMapping()
                    {
                        DeviceName = rootFileSystemPath,
                        Ebs = new EbsBlockDevice()
                        {
                            SnapshotId = rootFileSystemSnap,
                            DeleteOnTermination = true,
                            VolumeSize = defaultVolumeSize,
                            VolumeType = VolumeType.Gp2
                        }
                    }
                }
            });

            CheckStatusCode(response.HttpStatusCode);

            var instanceIds = new List<string>();

            foreach (var instance in response.Reservation.Instances)
            {
                instanceIds.Add(instance.InstanceId);
            }

            return instanceIds;
        }

        public async Task ResumeInstance(string instanceId)
        {
            ThrowIfNotConfigured();

            var response = await ec2Client.StartInstancesAsync(new StartInstancesRequest()
            {
                InstanceIds = new List<string>()
            });

            CheckStatusCode(response.HttpStatusCode);

            if (response.StartingInstances.All(t => t.InstanceId != instanceId))
                throw new Exception("EC2 server failed to be resumed");
        }

        public async Task<List<Instance>> GetInstanceStatuses(List<string> instanceIds,
            CancellationToken cancellationToken)
        {
            ThrowIfNotConfigured();

            var response = await ec2Client.DescribeInstancesAsync(new DescribeInstancesRequest()
            {
                InstanceIds = instanceIds
            }, cancellationToken);

            return response.Reservations.SelectMany(r => r.Instances).ToList();
        }

        public async Task TerminateInstance(string instanceId)
        {
            ThrowIfNotConfigured();

            var response = await ec2Client.TerminateInstancesAsync(new TerminateInstancesRequest()
            {
                InstanceIds = new List<string>() { instanceId }
            });

            CheckStatusCode(response.HttpStatusCode);

            if (response.TerminatingInstances.All(t => t.InstanceId != instanceId))
                throw new Exception("EC2 server failed to be terminated");
        }

        public async Task StopInstance(string instanceId)
        {
            ThrowIfNotConfigured();

            var response = await ec2Client.StopInstancesAsync(new StopInstancesRequest()
            {
                InstanceIds = new List<string>() { instanceId },
                Hibernate = true
            });

            CheckStatusCode(response.HttpStatusCode);

            if (response.StoppingInstances.All(t => t.InstanceId != instanceId))
                throw new Exception("EC2 server failed to be stopped");
        }

        protected void CheckStatusCode(HttpStatusCode code)
        {
            switch (code)
            {
                case HttpStatusCode.OK:
                case HttpStatusCode.Created:
                case HttpStatusCode.NoContent:
                    return;
            }

            throw new Exception($"EC2 request failed with status: {code}");
        }

        protected void ThrowIfNotConfigured()
        {
            if (!Configured)
                throw new Exception("EC2 access is not configured");
        }
    }
}
