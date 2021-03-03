// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Microsoft.DotNet.XHarness.Common.Logging
{
    /// <summary>
    /// Class that dynamically updates shows tests as they are being executed.
    /// </summary>
    public class TestResultConsoleReporter
    {
        private readonly ILog _testLog;
        private readonly StringBuilder _buffer = new StringBuilder();
        private int _lastLoggedLineLength = 0;

        public TestResultConsoleReporter()
        {
            _testLog = new CallbackLog(ProcessData);
        }

        public ILog AssignLog<T>(T log) where T : ILog => Log.CreateAggregatedLog(log, _testLog);

        public IFileBackedLog AssignLog(IFileBackedLog log) => Log.CreateReadableAggregatedLog(log, _testLog);

        private void ProcessData(string data)
        {
            if (!data.Contains(Environment.NewLine))
            {
                _buffer.Append(data);
                return;
            }

            var lines = data.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length - 1; i++)
            {
                _buffer.Append(lines[i]);
                ProcessLine(_buffer.ToString());
                _buffer.Clear();
            }

            // Did we receive a full line exactly?
            var last = lines.LastOrDefault();
            if (string.IsNullOrEmpty(last))
            {
                ProcessLine(_buffer.ToString());
                _buffer.Clear();
            }
            else
            {
                _buffer.Append(last);
            }
        }

        private void ProcessLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            if (!line.Contains("<test"))
            {
                return;
            }

            if (!line.Contains("/>"))
            {
                var position = line.LastIndexOf('>');
                line = line.Substring(0, position) + '/' + line.Substring(position);
            }

            try
            {
                var element = XElement.Parse(line);

                var testName = element.Attribute("name");
                var result = element.Attribute("result");

                if (testName == null || result == null)
                {
                    return;
                }

                if (_lastLoggedLineLength != 0)
                {
                    Console.Write("\r");
                }

                switch (result.Value.ToLowerInvariant())
                {
                    case "pass":
                        Console.Write("[ OK ]", ConsoleColor.Green);
                        break;

                    case "fail":
                        Console.Write("[FAIL]", ConsoleColor.Red);
                        break;

                    case "skip":
                        Console.Write("[SKIP]", ConsoleColor.Yellow);
                        break;

                    default:
                        Console.Write("[????]");
                        break;
                }

                Console.Write(" " + testName.Value);
                _lastLoggedLineLength = 6 + 1 + testName.Value.Length;
            }
            catch
            {
            }
        }
    }

    internal static class ConsoleEx
    {
        public static void Write(string message, ConsoleColor color = ConsoleColor.White)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(message);
            Console.ForegroundColor = originalColor;
        }

        public static void WriteLine(string message, ConsoleColor color = ConsoleColor.White) =>
            Write(message + Environment.NewLine, color);
    }
}
