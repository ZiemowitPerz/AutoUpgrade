using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dzidek.Net.AutoUpgrade.Upgrader.FileUtils
{
    public class FileMd5
    {
        public required string FileRelativePath { get; set; } 
        public required string Md5 { get; set; }
    }

}
