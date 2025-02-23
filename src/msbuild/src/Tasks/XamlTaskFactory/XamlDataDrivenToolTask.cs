﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;
using System.Text;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;

#nullable disable

namespace Microsoft.Build.Tasks.Xaml
{
    /// <summary>
    /// Part of the base class for tasks generated by the Xaml task factory.
    /// </summary>
    public abstract class XamlDataDrivenToolTask : ToolTask
    {
        /// <summary>
        /// True if we returned our commands directly from the command line generation and do not need to use the
        /// response file (because the command-line is short enough)
        /// </summary>
        private bool _skipResponseFileCommandGeneration;

        /// <summary>
        /// The task logging helper
        /// </summary>
        private TaskLoggingHelper _logPrivate;

        /// <summary>
        /// The command line for this task. 
        /// </summary>
        private string _commandLine;

        /// <summary>
        /// Constructor called by the generated task.
        /// </summary>
        protected XamlDataDrivenToolTask(string[] switchOrderList, ResourceManager taskResources)
            : base(taskResources)
        {
            InitializeLogger(taskResources);
            SwitchOrderList = switchOrderList;

            _logPrivate = new TaskLoggingHelper(this)
            {
                TaskResources = AssemblyResources.PrimaryResources,
                HelpKeywordPrefix = "MSBuild."
            };
        }

        /// <summary>
        /// The command-line template to use, if any.
        /// </summary>
        public string CommandLineTemplate { get; set; }

        /// <summary>
        /// The additional options that have been set. These are raw switches that
        /// go last on the command line.
        /// </summary>
        public string AdditionalOptions { get; set; } = String.Empty;

        /// <summary>
        /// Retrieves the list of acceptable non-zero exit codes.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1702:CompoundWordsShouldBeCasedCorrectly", MessageId = "NonZero", Justification = "Already shipped as public API")]
        public virtual string[] AcceptableNonZeroExitCodes { get; set; }

        /// <summary>
        /// Gets or set the dictionary of active tool switch values.
        /// </summary>
        public Dictionary<string, CommandLineToolSwitch> ActiveToolSwitchesValues { get; set; } = new Dictionary<string, CommandLineToolSwitch>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ordered list of switches
        /// </summary>
        /// <returns>Switches in declaration order</returns>
        internal virtual IEnumerable<string> SwitchOrderList { get; }

        /// <summary>
        /// The list of all the switches that have been set
        /// </summary>
        protected internal Dictionary<string, CommandLineToolSwitch> ActiveToolSwitches { get; } = new Dictionary<string, CommandLineToolSwitch>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Overridden to use UTF16, which works better than UTF8 for older versions of CL, LIB, etc. 
        /// </summary>
        protected override Encoding ResponseFileEncoding { get; } = Encoding.Unicode;

        /// <summary>
        /// Made a property to abstract out the "if null, call GenerateCommands()" logic. 
        /// </summary>
        private string CommandLine
        {
            get => _commandLine ?? (_commandLine = GenerateCommands());
            set => _commandLine = value;
        }

        /// <summary>
        /// Returns true if the property has a value in the list of active tool switches
        /// </summary>
        public bool IsPropertySet(string propertyName)
        {
            return !String.IsNullOrEmpty(propertyName) && ActiveToolSwitches.ContainsKey(propertyName);
        }

        /// <summary>
        /// Replace an existing switch with the specifed one of the same name. 
        /// </summary>
        public void ReplaceToolSwitch(CommandLineToolSwitch switchToAdd)
        {
            ActiveToolSwitches[switchToAdd.Name] = switchToAdd;
        }

        /// <summary>
        /// Add the value for a switch to the list of active values
        /// </summary>
        public void AddActiveSwitchToolValue(CommandLineToolSwitch switchToAdd)
        {
            if (switchToAdd.Type != CommandLineToolSwitchType.Boolean || switchToAdd.BooleanValue)
            {
                if (switchToAdd.SwitchValue != String.Empty)
                {
                    ActiveToolSwitchesValues.Add(switchToAdd.SwitchValue, switchToAdd);
                }
            }
            else
            {
                if (switchToAdd.ReverseSwitchValue != String.Empty)
                {
                    ActiveToolSwitchesValues.Add(switchToAdd.ReverseSwitchValue, switchToAdd);
                }
            }
        }

        /// <summary>
        /// Override Execute so that we can close the event handle we've created
        /// </summary>
        public override bool Execute()
        {
            if (!String.IsNullOrEmpty(CommandLineTemplate))
            {
                UseCommandProcessor = true;
            }
            else
            {
                if (String.IsNullOrEmpty(ToolExe))
                {
                    Log.LogError(ResourceUtilities.GetResourceString("Xaml.RuleMissingToolName"));
                    return false;
                }
            }


            bool success = base.Execute();
            return success;
        }

        /// <summary>
        /// For testing purposes only
        /// Returns the generated command line
        /// </summary>
        internal string GetCommandLine_ForUnitTestsOnly()
        {
            return GenerateResponseFileCommands();
        }

        /// <summary>
        /// Checks to see if the switch name is empty
        /// </summary>
        internal bool HasSwitch(string propertyName)
        {
            if (IsPropertySet(propertyName))
            {
                return !String.IsNullOrEmpty(ActiveToolSwitches[propertyName].Name);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Determine if the return value is in the list of acceptable exit codes.
        /// </summary>
        internal bool IsAcceptableReturnValue()
        {
            if (AcceptableNonZeroExitCodes != null)
            {
                foreach (string acceptableExitCode in AcceptableNonZeroExitCodes)
                {
                    if (ExitCode == Convert.ToInt32(acceptableExitCode, CultureInfo.InvariantCulture))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Validate the data
        /// </summary>
        internal void PostProcessSwitchList()
        {
            ValidateRelations();
            ValidateOverrides();
        }

        /// <summary>
        /// Validate relationships.
        /// </summary>
        internal void ValidateRelations()
        {
            // do nothing by default.
        }

        /// <summary>
        /// Validate the overrides.
        /// </summary>
        internal void ValidateOverrides()
        {
            List<string> overriddenSwitches = new List<string>();

            // Collect the overrided switches
            foreach (KeyValuePair<string, CommandLineToolSwitch> overriddenSwitch in ActiveToolSwitches)
            {
                foreach (KeyValuePair<string, string> overridePair in overriddenSwitch.Value.Overrides)
                {
                    if (String.Equals(overridePair.Key, (overriddenSwitch.Value.Type == CommandLineToolSwitchType.Boolean && !overriddenSwitch.Value.BooleanValue) ? overriddenSwitch.Value.ReverseSwitchValue.TrimStart('/') : overriddenSwitch.Value.SwitchValue.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (KeyValuePair<string, CommandLineToolSwitch> overrideTarget in ActiveToolSwitches)
                        {
                            if (!String.Equals(overrideTarget.Key, overriddenSwitch.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                if (String.Equals(overrideTarget.Value.SwitchValue.TrimStart('/'), overridePair.Value, StringComparison.OrdinalIgnoreCase))
                                {
                                    overriddenSwitches.Add(overrideTarget.Key);
                                    break;
                                }
                                else if ((overrideTarget.Value.Type == CommandLineToolSwitchType.Boolean) && (!overrideTarget.Value.BooleanValue) && String.Equals(overrideTarget.Value.ReverseSwitchValue.TrimStart('/'), overridePair.Value, StringComparison.OrdinalIgnoreCase))
                                {
                                    overriddenSwitches.Add(overrideTarget.Key);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // Remove the overridden switches
            foreach (string overridenSwitch in overriddenSwitches)
            {
                ActiveToolSwitches.Remove(overridenSwitch);
            }
        }

        /// <summary>
        /// Creates the command line and returns it as a string by:
        /// 1. Adding all switches with the default set to the active switch list
        /// 2. Customizing the active switch list (overridden in derived classes)
        /// 3. Iterating through the list and appending switches 
        /// </summary>
        protected override string GenerateResponseFileCommands()
        {
            if (_skipResponseFileCommandGeneration)
            {
                _skipResponseFileCommandGeneration = false;
                return null;
            }
            else
            {
                return CommandLine;
            }
        }

        /// <summary>
        /// Allows tool to handle the return code.
        /// This method will only be called with non-zero exitCode. If the non zero code is an acceptable one then we return true
        /// </summary>
        /// <returns>The return value of this method will be used as the task return value</returns>
        protected override bool HandleTaskExecutionErrors()
        {
            if (IsAcceptableReturnValue())
            {
                return true;
            }

            // We don't want to use ToolTask's implementation because it doesn't report the command line that failed. 
            if (ExitCode == NativeMethods.SE_ERR_ACCESSDENIED)
            {
                _logPrivate.LogErrorWithCodeFromResources("Xaml.CommandFailedAccessDenied", CommandLine, ExitCode);
            }
            else
            {
                _logPrivate.LogErrorWithCodeFromResources("Xaml.CommandFailed", CommandLine, ExitCode);
            }
            return false;
        }

        /// <summary>
        /// Generates the command line for the tool.
        /// </summary>
        private string GenerateCommands()
        {
            PostProcessSwitchList();

            var generator =
                new CommandLineGenerator(ActiveToolSwitches, SwitchOrderList)
                {
                    CommandLineTemplate = CommandLineTemplate,
                    AdditionalOptions = AdditionalOptions
                };

            CommandLine = generator.GenerateCommandLine();
            return CommandLine;
        }

        /// <summary>
        /// A method that will validate the integer type arguments
        /// If the min or max is set, and the value a property is set to is not within
        /// the range, the build fails
        /// </summary>
        public bool ValidateInteger(string switchName, int min, int max, int value)
        {
            if (value < min || value > max)
            {
                _logPrivate.LogErrorWithCodeFromResources("Xaml.ArgumentOutOfRange", switchName, value);
                return false;
            }

            return true;
        }

        /// <summary>
        /// A method for the enumerated values a property can have
        /// This method checks the value a property is set to, and finds the corresponding switch
        /// </summary>
        /// <returns>The switch that a certain value is mapped to</returns>
        public string ReadSwitchMap(string propertyName, string[][] switchMap, string value)
        {
            if (switchMap != null)
            {
                foreach (string[] switches in switchMap)
                {
                    if (String.Equals(switches[0], value, StringComparison.OrdinalIgnoreCase))
                    {
                        return switches[1];
                    }
                }

                _logPrivate.LogErrorWithCodeFromResources("Xaml.ArgumentOutOfRange", propertyName, value);
            }

            return String.Empty;
        }

        /// <summary>
        /// A method for the enumerated values a property can have
        /// This method checks the value a property is set to, and finds the corresponding switch
        /// </summary>
        /// <returns>The switch that a certain value is mapped to</returns>
        public int ReadSwitchMap2(string propertyName, Tuple<string, string, Tuple<string, bool>[]>[] switchMap, string value)
        {
            if (switchMap != null)
            {
                for (int i = 0; i < switchMap.Length; ++i)
                {
                    if (String.Equals(switchMap[i].Item1, value, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }

                _logPrivate.LogErrorWithCodeFromResources("Xaml.ArgumentOutOfRange", propertyName, value);
            }

            return -1;
        }

        /// <summary>
        /// Gets a switch value by concatenating the switch's base value (usually the switch itself) with its argument, if any.
        /// </summary>
        public string CreateSwitchValue(string propertyName, string baseSwitch, string separator, Tuple<string, bool>[] arguments)
        {
            var switchValue = new StringBuilder(baseSwitch);
            foreach (Tuple<string, bool> argument in arguments)
            {
                string argName = argument.Item1;
                bool isRequired = argument.Item2;

                if (!String.IsNullOrEmpty(argName))
                {
                    if (!IsPropertySet(argName))
                    {
                        if (isRequired)
                        {
                            _logPrivate.LogErrorWithCodeFromResources("Xaml.MissingRequiredArgument", propertyName, argName);
                            throw new ArgumentException(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("Xaml.MissingRequiredArgument", propertyName, argName));
                        }
                    }
                    else
                    {
                        switchValue.Append(separator).Append(ActiveToolSwitches[argName]);
                    }
                }
            }

            return baseSwitch;
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        internal void InitializeLogger(ResourceManager taskResources)
        {
            _logPrivate = new TaskLoggingHelper(this)
            {
                TaskResources = AssemblyResources.PrimaryResources,
                HelpKeywordPrefix = "MSBuild."
            };
        }

        #region ToolTask Members

        /// <summary>
        /// This method is called to find the tool if ToolPath wasn't specified.
        /// We just return the name of the tool so it can be found on the path.
        /// Deriving classes can choose to do something else.
        /// </summary>
        protected override string GenerateFullPathToTool()
        {
            return ToolName;
        }

        /// <summary>
        /// Validates all of the set properties that have either a string type or an integer type
        /// </summary>
        protected override bool ValidateParameters()
        {
            return !_logPrivate.HasLoggedErrors && !Log.HasLoggedErrors;
        }

        #endregion

        /// <summary>
        /// Generate the command line if it is less than 32k.
        /// </summary>
        protected override string GenerateCommandLineCommands()
        {
            // If the command is too long, it will most likely fail. The command line
            // arguments passed into any process cannot exceed 32768 characters, but
            // depending on the structure of the command (e.g. if it contains embedded
            // environment variables that will be expanded), longer commands might work,
            // or shorter commands might fail -- to play it safe, we warn at 32000.
            // NOTE: cmd.exe has a buffer limit of 8K, but we're not using cmd.exe here,
            // so we can go past 8K easily.
            if (CommandLine.Length < 32000)
            {
                _skipResponseFileCommandGeneration = true;
                return CommandLine;
            }

            _skipResponseFileCommandGeneration = false;
            return null;
        }
    }
}
