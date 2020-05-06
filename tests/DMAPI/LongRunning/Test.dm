/world/New()
	TgsNew(new /datum/tgs_event_handler/impl, TGS_SECURITY_ULTRASAFE)
	StartAsync()

/proc/StartAsync()
	set waitfor = FALSE
	Run()

/proc/Run()
	sleep(60)
	world.TgsChatBroadcast("World Initialized")
	world.TgsInitializationComplete()

/world/Topic(T, Addr, Master, Keys)
	TGS_TOPIC

	var/list/data = params2list(T)
	var/special_tactics = data["tgs_integration_test_special_tactics"]
	if(special_tactics)
		RebootAsync()
		return "ack"
	return "feck"

/world/Reboot(reason)
	TgsChatBroadcast("World Rebooting")
	TgsReboot()

/datum/tgs_event_handler/impl/HandleEvent(event_code, ...)
	set waitfor = FALSE

	world.TgsChatBroadcast("Recieved event: [json_encode(args)]")

/proc/RebootAsync()
	set waitfor = FALSE
	world.TgsChatBroadcast("Rebooting after 3 seconds");
	world.sleep_offline = FALSE
	sleep(30)
	world.Reboot()
