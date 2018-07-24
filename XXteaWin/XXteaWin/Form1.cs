using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xxtea;

namespace XXteaWin
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void btnEncrypt_Click(object sender, EventArgs e)
        {
            var sd = openFileDialog1.ShowDialog();
            if (sd == DialogResult.OK)
            {
                String key = "1234567890";
                using (var fs = openFileDialog1.OpenFile())
                {
                    byte[] buffer = new byte[fs.Length];
                    fs.Read(buffer, 0, buffer.Length);
                    var data = XXTEA.Encrypt(buffer, key);
                    var file = new FileInfo(openFileDialog1.FileName);
                    string fname = Path.Combine(file.DirectoryName,
                        Path.GetFileNameWithoutExtension(openFileDialog1.FileName) + "_secure" +
                        Path.GetExtension(openFileDialog1.FileName));
                    if (File.Exists(fname))
                        File.Delete(fname);
                    using (var fs2 = File.Create(fname))
                    {
                        fs2.Write(data, 0, data.Length);
                    }
                }
            }
        }
    }
}
