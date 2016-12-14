using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFDeploy.Models
{
    [ImplementPropertyChanged]
    public class DeployFile
    {
        public string FileName { get; set; }

        public string FilePath { get; set; }

        public ulong FileBaseAddress { get; set; }

        public ulong FileSize { get; set; }

        public string FileTimeStamp { get; set; }

        public bool Selected { get; set; }

        public DeployFile(string fileName, string filePath, ulong fileBaseAddress, ulong fileSize, string fileTimeStamp, bool selected = true)
        {
            FileName = fileName;
            FilePath = filePath;
            FileBaseAddress = fileBaseAddress;
            FileSize = fileSize;
            FileTimeStamp = fileTimeStamp;
            Selected = selected;
        }
    }
}
