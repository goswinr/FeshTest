﻿namespace Seff

open System
open System.Windows

open Seff.Views
open Seff.Config
open Seff.Util.General


module Initialize =  
    let everything(context:HostingMode, startupArgs:string[])=
        Timer.InstanceStartup.tic()             // optional timer for full init process
        Sync.installSynchronizationContext()    // do first

        let en_US = Globalization.CultureInfo.CreateSpecificCulture("en-US")        
        Globalization.CultureInfo.DefaultThreadCurrentCulture   <- en_US
        Globalization.CultureInfo.DefaultThreadCurrentUICulture <- en_US
        

        Controls.ToolTipService.ShowOnDisabledProperty.OverrideMetadata(  typeof<Controls.Control>, new FrameworkPropertyMetadata(true)) // to still show-tooltip-when a button(or menu item )  is disabled-by-command //https://stackoverflow.com/questions/4153539/wpf-how-to-show-tooltip-when-button-disabled-by-command
        Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(    typeof<DependencyObject>, new FrameworkPropertyMetadata(Int32.MaxValue))
        Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof<DependencyObject>, new FrameworkPropertyMetadata(50))

        /// ------------------ Log and Config --------------------
        
        let log = new Log()        
        let config = new Config(log,context,startupArgs)
        log.AdjustToSettingsInConfig(config)
        

        //--------------ERROR Handeling --------------------
        if notNull Application.Current then // null if application is not yet created, or no application in hoted context
            Application.Current.DispatcherUnhandledException.Add(fun e ->  
                if e <> null then 
                    log.PrintDebugMsg "Application.Current.DispatcherUnhandledException in main Thread: %A" e.Exception           
                    e.Handled<- true)
        
        //catching unhandled exceptions generated from all threads running under the context of a specific application domain. 
        //https://dzone.com/articles/order-chaos-handling-unhandled
        //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it        
        AppDomain.CurrentDomain.UnhandledException.AddHandler (  new UnhandledExceptionEventHandler( ProcessCorruptedState(config).Handler)) 

        // --------------- rest of views-------------------

        Seff( config, log)

        
        
        

        


        