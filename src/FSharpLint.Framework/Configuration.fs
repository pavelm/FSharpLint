﻿(*
    FSharpLint, a linter for F#.
    Copyright (C) 2014 Matthew Mcveigh

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*)

namespace FSharpLint.Framework

open FSharp.Data

module Configuration =

    type Config = XmlProvider<"""<FSharpLintSettings>
    <Analysers>
        <Analyser AnalyserId="FSharpLint.NamingRules">
            <Rules>
                <Rule Name="InterfaceNamesMustBeginWithI">
                    <RuleSettings>
                        <Property name="Enabled">True</Property>
                    </RuleSettings>
                </Rule>
                <Rule Name="InterfaceNamesMustBeginWithI">
                    <RuleSettings>
                        <Property name="Enabled">True</Property>
                    </RuleSettings>
                </Rule>
            </Rules>
            <AnalyserSettings />
        </Analyser>
        <Analyser AnalyserId="FSharpLint.NamingRules">
            <Rules />
            <AnalyserSettings>
                <Property name="Enabled">True</Property>
                <Property name="MaxParameters">8</Property>
            </AnalyserSettings>
        </Analyser>
    </Analysers>
</FSharpLintSettings>""", Global = true>

    exception ConfigurationException of string

    type Property =
        | Enabled of bool
        | Lines of int
        | MaxParameters of int

    type Rule = 
        {
            Settings: Map<string, Property>
        }

    type Analyser = 
        { 
            Rules: Map<string, Rule> 
            Settings: Map<string, Property>
        }

    let private parseProperty (property:Config.DomainTypes.Property) =
        match property.Name with
            | "Enabled" when property.BooleanValue.IsSome -> 
                Some(Enabled(property.BooleanValue.Value))
            | "Lines" when property.NumberValue.IsSome -> 
                Some(Lines(property.NumberValue.Value))
            | "MaxParameters" when property.NumberValue.IsSome -> 
                Some(MaxParameters(property.NumberValue.Value)) 
            | _ -> 
                None

    let private parseSettings properties =
        [
            for property in properties do
                match parseProperty property with
                    | Some(propertyVal) -> 
                        yield (property.Name, propertyVal) 
                    | None -> ()
        ]
        
    let private parseRule (rule:Config.DomainTypes.Rule) : Rule =
        {
            Settings = 
                [ 
                    let ruleSettings = rule.GetRuleSettings()

                    if ruleSettings.Length <> 1 then
                        let error = "Rule elements must only include one RuleSettings element. Rule " + rule.Name + " contained more than one."
                        raise (ConfigurationException(error)) 
                    else
                        yield! ruleSettings.[0].GetProperties() |> parseSettings
                ]
                    |> Map.ofList
        }

    let private getRules (analyser:Config.DomainTypes.Analyser) =
        [ 
            for rules in analyser.GetRules() do 
                for rule in rules.GetRules() do
                    yield (rule.Name, parseRule rule) 
        ]
            |> Map.ofList

    /// Parse a configuration file.
    let configuration file = 
        let config = Config.Parse(file)
        [ 
            for analyser in config.Analysers.GetAnalysers() do 
                let analyserSettings = analyser.GetAnalyserSettings()

                if analyserSettings.Length <> 1 then
                    let error = "Analyser elements must only include one AnalyserSettings element. Analyser " + analyser.AnalyserId + " contained more than one."
                    raise (ConfigurationException(error)) 
                else
                    let settings = analyserSettings.[0].GetProperties() |> parseSettings |> Map.ofList

                    yield (analyser.AnalyserId, { Rules = getRules analyser; Settings = settings }) 
        ]
            |> Map.ofList

    let overwriteMap (oldMap:Map<'a,'b>) (newMap:Map<'a,'b>) overwriteValue =
        [ 
            for keyValuePair in oldMap do
                if newMap |> Map.containsKey keyValuePair.Key then
                    yield (keyValuePair.Key, overwriteValue keyValuePair.Value newMap.[keyValuePair.Key])
                else
                    yield (keyValuePair.Key, keyValuePair.Value)
        ]
            |> Map.ofList

    let private overrideRuleSettings oldProperty newProperty = newProperty

    let private overrideRule (oldRule:Rule) (newRule:Rule) : Rule =
        { 
            Settings = overwriteMap oldRule.Settings newRule.Settings overrideRuleSettings 
        }

    let private overrideAnalysers oldRules newRules =
        { 
            Rules = overwriteMap oldRules.Rules newRules.Rules overrideRule 
            Settings = overwriteMap oldRules.Settings newRules.Settings overrideRuleSettings
        }

    let overrideConfiguration configToOverride file =
        let newAnalysers = configuration file

        overwriteMap configToOverride newAnalysers overrideAnalysers
        
    let loadDefaultConfiguration () =
        System.IO.File.ReadAllText("DefaultConfiguration.FSharpLint") |> configuration