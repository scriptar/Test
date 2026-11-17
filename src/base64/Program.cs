using System;
using System.IO;
using System.Text;

namespace base64decode
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine($"Usage: {AppDomain.CurrentDomain.FriendlyName} <inputfile>");
                return;
            }
            var inputFile = args[0];
            try
            {
                var base64Input = File.ReadAllText(inputFile);
                var bytes = Convert.FromBase64String(base64Input);
                var decoded = Encoding.UTF8.GetString(bytes);
                Console.WriteLine(decoded);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"File not found: {inputFile}");
            }
            catch (FormatException)
            {
                Console.WriteLine("Invalid Base64 input.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
