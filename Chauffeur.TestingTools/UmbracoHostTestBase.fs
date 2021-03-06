﻿namespace Chauffeur.TestingTools

open System
open System.IO
open Chauffeur.Host
open System.Reflection

module ChauffeurSetup =
    open System.Text.RegularExpressions

    let private cwd = FileInfo(Assembly.GetExecutingAssembly().Location).Directory.FullName
    let private dbFolder = "databases"

    let internal setDataDirectory() =
        let now = DateTimeOffset.Now
        let ticks = now.Ticks.ToString()

        let folderForRun = Path.Combine [|cwd; dbFolder; ticks|]

        Directory.CreateDirectory folderForRun |> ignore

        AppDomain.CurrentDomain.SetData("DataDirectory", folderForRun)

        folderForRun

    let private Log4NetAssemblyPattern = new Regex("log4net, Version=([\\d\\.]+?), Culture=neutral, PublicKeyToken=\\w+$", RegexOptions.Compiled)
    let private Log4NetReplacement = "log4net, Version=2.0.8.0, Culture=neutral, PublicKeyToken=669e0ddf0bb1aa2a"

    let fixLog4Net = ResolveEventHandler(fun o args ->
                        match (args.Name |> Log4NetAssemblyPattern.IsMatch) && args.Name <> Log4NetReplacement with
                        | true -> Assembly.Load(Log4NetAssemblyPattern.Replace(args.Name, Log4NetReplacement))
                        | false -> null
                    )

    AppDomain.CurrentDomain.add_AssemblyResolve fixLog4Net

    let fixAutoMapper = ResolveEventHandler(fun o args -> 
                            match args.Name.StartsWith("AutoMapper") && args.Name.EndsWith("PublicKeyToken=null") with
                            | true -> Assembly.Load(args.Name.Replace(", PublicKeyToken=null", ", PublicKeyToken=be96cd2c38ef1005"))
                            | false -> null
                        )
    AppDomain.CurrentDomain.add_AssemblyResolve fixAutoMapper

[<AbstractClass>]
type UmbracoHostTestBase() =
    let dbFolder = ChauffeurSetup.setDataDirectory()

    let writer = new MockTextWriter()
    let reader = new MockTextReader()
    let host = new UmbracoHost(reader, writer)

    /// <summary>
    /// The temp path that was generated for the Umbraco `App_Data` folder, and where the Umbraco database will
    /// if you use the SQL CE database provider
    /// </summary>
    member x.DatabaseLocation = dbFolder

    /// <summary>
    /// The Chauffeur host to run Chauffeur deliverables again
    /// </summary>
    member x.Host = host

    /// <summary>
    /// An output stream that you can read the messages Chauffeur writes to
    /// </summary>
    member x.TextReader = reader

    /// <summary>
    /// An input stream that Chauffeur will read from
    /// </summary>
    member x.TextWriter = writer

    /// <summary>
    /// Installs the Umbraco database using the Chauffeur `install` deliverable.
    /// If you are using SQL CE it'll also create the file for you.
    /// </summary>
    member x.InstallUmbraco() =
        [| "install y" |] |> x.Host.Run

    /// <summary>
    /// Gets the path on disk that Chauffeur would look for packages/delivery/etc. files within.
    /// This is the path that Chauffeur will resolve from its settings API internally.
    /// </summary>
    member __.GetChauffeurFolder() =
        let chauffeurFolder = Path.Combine [| dbFolder; "Chauffeur" |]

        match (Directory.Exists chauffeurFolder) with
        | true -> DirectoryInfo chauffeurFolder
        | false -> Directory.CreateDirectory chauffeurFolder

    member x.CreatePackage packageName packageContents =
        let chauffeurFolder = x.GetChauffeurFolder()
        let packageFilename = sprintf "%s.xml" packageName
        let filePath =
            Path.Combine [| chauffeurFolder.FullName
                            packageFilename |]
        File.WriteAllText(filePath, packageContents)
        packageFilename

    member __.GetSiteRootFolder() =
        let asm = Assembly.GetAssembly(host.GetType())
        let dir = (new FileInfo(asm.Location)).Directory.FullName
        Path.Combine(dir, "..")

    interface IDisposable with
        member x.Dispose() =
            writer.Dispose()
            reader.Dispose()
            host.Dispose()
