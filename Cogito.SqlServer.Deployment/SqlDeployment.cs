﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Cogito.SqlServer.Deployment
{

    /// <summary>
    /// Describes a loaded SQL deployment manifest and provides the capability to execute targets.
    /// </summary>
    public class SqlDeployment
    {

        /// <summary>
        /// Loads a <see cref="SqlDeployment"/> from the specified <see cref="XDocument"/>.
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        public static SqlDeployment Load(XDocument xml) => SqlDeploymentXmlReader.Load(xml);

        /// <summary>
        /// Loads a <see cref="SqlDeployment"/> from the specified <see cref="XElement"/>.
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        public static SqlDeployment Load(XElement xml) => SqlDeploymentXmlReader.Load(xml);

        /// <summary>
        /// Loads a <see cref="SqlDeployment"/> from the specified <see cref="XmlReader"/>.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static SqlDeployment Load(XmlReader reader) => SqlDeploymentXmlReader.Load(reader);

        /// <summary>
        /// Gets the path to the loaded manifest, if available.
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// Gets the parameters that can be passed to the deployment.
        /// </summary>
        public ICollection<SqlDeploymentParameter> Parameters { get; set; } = new List<SqlDeploymentParameter>();

        /// <summary>
        /// Gets the set of targets included within the deployment.
        /// </summary>
        public ICollection<SqlDeploymentTarget> Targets { get; } = new List<SqlDeploymentTarget>();

        /// <summary>
        /// Compiles the <see cref="SqlDeployment"/> into a <see cref="SqlDeploymentPlan"/>.
        /// </summary>
        /// <param name="arguments"></param>
        /// <returns></returns>
        public SqlDeploymentPlan Compile(IDictionary<string, string> arguments = null, string relativeRoot = null)
        {
            // create a local copy of the arguments for modification
            var args = arguments != null ? new Dictionary<string, string>(arguments) : new Dictionary<string, string>();

            // populate missing arguments with default values
            foreach (var kvp in Parameters)
                if (args.ContainsKey(kvp.Name) == false)
                    args[kvp.Name] = kvp.DefaultValue;

            // check for missing parameters
            foreach (var kvp in args)
                if (kvp.Value == null)
                    throw new SqlDeploymentException($"Missing value for parameter '{kvp.Key}'.");

            // default value
            if (relativeRoot == null)
                relativeRoot = SourcePath != null ? Path.GetDirectoryName(SourcePath) : Environment.CurrentDirectory;

            // generate a deployment plan
            return new SqlDeploymentPlan(
                Targets.ToDictionary(
                    i => i.Name,
                    i => new SqlDeploymentPlanTarget(i.DependsOn.Select(j => j.Name).ToArray(), i.Compile(args, relativeRoot).ToArray())));
        }

    }

}
