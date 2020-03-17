﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

using McMaster.Extensions.CommandLineUtils;

using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;

namespace Cogito.SqlServer.Deployment.Tool
{

    /// <summary>
    /// Executes a SQL Server Deployment.
    /// </summary>
    [Command("deploy", Description = "Execute SQL Server Deployment")]
    class Deploy
    {

        /// <summary>
        /// Gets the parent program.
        /// </summary>
        public Program Parent { get; set; }

        /// <summary>
        /// Gets or sets the path to the SQL deployment manifest file.
        /// </summary>
        [Argument(0, "manifest", "deployment manifest file")]
        [Required]
        [FileExists]
        public string Manifest { get; set; }

        /// <summary>
        /// Gets or sets the set of targets to be executed by the deployment.
        /// </summary>
        [Argument(1, "target(s) to execute")]
        public List<string> Targets { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the parameter arguments to be passed to the deployment.
        /// </summary>
        [Option("-a <name>=<value> | --argument <name>=<value>", "argument to pass to deployment (<name>=<value>)", CommandOptionType.MultipleValue)]
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Executes the deployment.
        /// </summary>
        /// <returns></returns>
        public async Task<int> OnExecuteAsync(CommandLineApplication app, IConsole console)
        {
            using var f = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(Parent.Debug ? LogLevel.Trace : LogLevel.Information));
            var l = f.CreateLogger("SqlDeployment");

            var manifest = Path.IsPathFullyQualified(Manifest) == false ? Path.GetFullPath(Manifest, Environment.CurrentDirectory) : Manifest;
            if (File.Exists(manifest) == false)
            {
                l.LogError("Could not find SQL Deployment manifest file: {Manifest}.", manifest);
                return 1;
            }

            // extract parameters
            var arguments = Arguments
                .Select(i => i.Split(new[] { '=' }, 2))
                .ToDictionary(i => i[0], i => i.Length > 1 ? i[1] : null);

            // load SQL deployment
            var deployment = SqlDeployment.Load(XDocument.Load(manifest, LoadOptions.SetBaseUri));

            try
            {
                // compile plan from manifest and parameters
                var plan = deployment.Compile(arguments, Path.GetDirectoryName(manifest));

                // execute plan with specified targets
                if (Targets.Count > 0)
                    await new SqlDeploymentExecutor(plan, l).ExecuteAsync(Targets.ToArray());
                else
                    await new SqlDeploymentExecutor(plan, l).ExecuteAsync();
            }
            catch (SqlDeploymentException e)
            {
                l.LogError(e, "Unable to compile or execute SQL deployment.");
                return 1;
            }

            return 0;
        }

    }

}
