// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Ssf2Weasel.Cli;

Console.OutputEncoding = Encoding.UTF8;
return CliApplication.Run(args, Console.Out, Console.Error);
