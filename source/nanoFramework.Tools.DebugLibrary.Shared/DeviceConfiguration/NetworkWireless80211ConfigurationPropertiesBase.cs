using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.NetworkInformation;

namespace nanoFramework.Tools.Debugger
{
    public class NetworkWireless80211ConfigurationPropertiesBase : NetworkConfigurationPropertiesBase
    {
        public AuthenticationType Authentication { get; set; }
        public EncryptionType Encryption { get; set; }
        public RadioType Radio { get; set; }
        [MaxLength(32, ErrorMessage = "Maximum allowed length for SSID is 32.")]
        public string Ssid { get; set; }
        [MaxLength(64, ErrorMessage = "Maximum allowed length for network password is 64.")]
        public string Password { get; set; }
    }
}