# GodrejWMS – Putaway Preferences demo: narration script (final, with timestamps)

Voice: Microsoft Neural **en-IN-NeerjaNeural** (female, Indian English), rate +4%, via the free edge-tts client (no API key). Total runtime 5:19. Timestamps are mm:ss from the start of the video. Spoken text is shown with letter-spelling for acronyms (e.g. "A O P"); the on-screen captions use the normal spelling.

## Scene 1 – Introduction  (00:00)

- **00:00** Welcome to GodrejWMS. This short video shows how Putaway Preferences work.
- **00:08** You maintain Brand and A O P masters.
- **00:12** Then you map each brand to a preferred range of locations.
- **00:17** The system uses that range to suggest putaway locations for incoming stock.

## Scene 2 – Brand Master – Add  (00:25)

- **00:25** Open Brand Master from the Master Data menu.
- **00:29** This screen holds the brand codes used by the Material Master.
- **00:34** Click New Brand.
- **00:37** The Create Brand panel opens. Brand Code is mandatory, so enter Stella.
- **00:44** Add a description if you like.
- **00:48** Then click Save Brand.
- **00:51** The panel closes, and the new brand appears in the list as Active.

## Scene 3 – Brand Master – Update  (00:58)

- **00:58** To change a brand, search for it by code or description.
- **01:03** Then click the pencil icon on its row.
- **01:07** Change the description.
- **01:10** The Active checkbox lets you make a brand inactive, so it is no longer offered for materials.
- **01:18** Click Save Brand.
- **01:20** The grid now shows the updated description.

## Scene 4 – AOP Description Master – Add  (01:26)

- **01:26** Next, A O P Description Master.
- **01:30** It stores the A O P codes used by the Material Master.
- **01:35** Click New A O P Code.
- **01:39** A O P Code is mandatory, so enter Stella.
- **01:43** Add its description.
- **01:46** Click Save A O P Code.
- **01:50** The new code appears in the list as Active.

## Scene 5 – AOP Description Master – Update  (01:55)

- **01:55** Updating works the same way. Search for the code.
- **02:01** Click the pencil icon, and change the description.
- **02:05** Use the Active checkbox to retire a code you no longer need. Inactive codes are not offered when you maintain materials.
- **02:15** Click Save A O P Code.
- **02:18** The list shows the updated description.

## Scene 6 – Brand Location Preferences – Create Mapping  (02:24)

- **02:24** Now the key step. Open Brand Location Preferences.
- **02:29** At the moment, no brand has a preferred range.
- **02:34** Click New Mapping.
- **02:37** Choose the Brand Code. We will use Cinthol.
- **02:42** Then choose the Warehouse, G C P L, and the Rack, B1.
- **02:48** Now set the range. Location Preference From is B eleven zero one.
- **02:55** Location Preference To is B twelve zero three.
- **02:59** So the range covers columns eleven and twelve, and levels one to three, in that rack.
- **03:07** Click Save Mapping.
- **03:09** The mapping appears in the list, with its warehouse, rack and location range.
- **03:16** Use the pencil icon to edit a mapping, or the bin icon to remove it. A brand can have more than one mapping.

## Scene 7 – Impact on Inward Putaway – Before  (03:26)

- **03:26** Now, let us see the effect on inward. First, here is what happens without a mapping.
- **03:34** We receive a hundred boxes of a Cinthol material, and a hundred boxes of a G K material.
- **03:46** After Submit Inward, the Allocation Result shows where the system suggests putaway.
- **03:52** With no mapping, both materials are suggested in rack A, starting at A zero one zero one.

## Scene 7 – Impact on Inward Putaway – After  (04:02)

- **04:02** Now, the same receipt again, after saving the Cinthol mapping.
- **04:16** The Cinthol material is now suggested inside the mapped range, B eleven zero one and B eleven zero two.
- **04:24** The G K line has no mapping, so it falls back to the standard putaway logic, and stays in rack A.
- **04:32** The rule order is this. Inside the brand range, the system tops up locations holding the same S K U, then consolidates, then picks the best empty location.
- **04:45** Only if quantity remains does it look outside the range. So the mapping is a strong preference, not a hard block.

## Scene 8 – Summary and Best Practices  (04:56)

- **04:56** To keep this working well, keep your Brand and A O P masters clean and up to date.
- **05:02** Update the mappings whenever you add new brands, racks or locations.
- **05:08** And check capacity, because a range that is too small pushes stock outside it.
- **05:15** Thank you for watching.

