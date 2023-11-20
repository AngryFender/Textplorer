using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml;

namespace Textplorer
{
    [ValueConversion(typeof(string), typeof(object))]
    public class StringToXamlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
                string input = value as string;
                if (input != null)
                {
                    using (StringReader stringReader = new StringReader(input))
                    {
                        using (XmlReader xmlReader = XmlReader.Create(stringReader))
                        {
                            return XamlReader.Load(xmlReader);
                        }
                    }
                }
                return null;
        }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
