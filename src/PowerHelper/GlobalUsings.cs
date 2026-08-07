// Enabling UseWPF alongside UseWindowsForms changes the SDK-generated implicit usings set,
// dropping System.IO/System.Drawing/System.Windows.Forms that were previously implicit.
// Restored explicitly here instead of patching every affected file individually.
global using System.Drawing;
global using System.IO;
global using System.Windows.Forms;
