using System;
using System.IO;

namespace NifViewer.Lib
{
    public class NifViewer
    {
        public NifViewer(string path)
        {
            if (!File.Exists(path))
                throw new ArgumentException("File does not exist!", nameof(path));
        }
    }
}
