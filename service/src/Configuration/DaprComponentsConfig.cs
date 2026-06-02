using System.ComponentModel.DataAnnotations;

namespace Intropy.BusinessIncidentService.Configuration;

public class DaprComponentsConfig
{
    [Required]
    public required string InternalPubSubName { get; set; }
    [Required]
    public required string InternalTopicName { get; set; }
    [Required]
    public required string NotificationPubSubName { get; set; }
    [Required]
    public required string NotificationTopicName { get; set; }
}
