# SortQuest: related work and positioning

This brief summarizes research and products related to SortQuest, gathered by the team before building.
Use it to write the README's "How SortQuest is different" section.
Rules for using it:
- Describe other work accurately and briefly, in our own words. Don't quote.
- Never claim SortQuest is the "first" of anything. Say "we haven't found" instead.
- Link sources where a link is given. Items marked "verify" should be described without a citation unless someone confirms the source.

## 1. Games that teach people to sort waste
- **EcoQuestVR:** a VR game where players sort waste arriving on a conveyor belt into bins. The goal is to teach people.
- **SEPBO:** a research serious game for waste sorting, evaluated on whether it improves players' own sorting skills.
- Many other recycling games exist, mostly for education.

**How we differ:** these games use sorting to teach humans. SortQuest uses human sorting to teach a robot. We also care about *how* a person grips each item, not just which bin it goes in. Players still learn to sort as a side effect.

## 2. Crowdsourcing and gamifying robot training data
- **Project Kitchen** (arXiv, September 2026): https://arxiv.org/html/2609.18650
  A gamified VR platform that collects manipulation data without robot hardware, for kitchen tasks. It extracts robot-independent cues (like where and how objects are grasped) from gameplay, pre-trains robot policies on that data, then fine-tunes with a handful of real robot demonstrations. It reported about a 10-point improvement in simulated success rates and transfer to real robots with few demos. This is the closest work to SortQuest, and it shows the approach works.
- **RoboCade** (arXiv): https://arxiv.org/pdf/2512.21235
  Adds game mechanics to remote teleoperation of real robots to collect demonstrations.
- **RoboTurk** (Stanford, 2018): crowdsourced robot demonstrations by letting remote users steer robots with a smartphone. Built for general manipulation tasks, with workers controlling the robot directly.
- **Universal Manipulation Interface (UMI)** (Stanford and Columbia, 2024): a handheld gripper people use to record robot demonstrations in the real world without needing a robot there.
- **Online puzzle game for robot grasping** (a 2021 study, verify): players labeled properties of unknown objects through a game, which helped a robot decide what it could pick up, such as bottles.

**How we differ:**
- **Domain:** recycling, not kitchens or general tasks. That includes rare, dangerous items like lithium-ion batteries, and the cold-start problem for new robots, facilities, and packaging.
- **Interaction:** players grab items naturally with their own hands. There's no teleoperation, controller mapping, or robot hardware, so anyone at a booth can contribute.
- **Hand-as-gripper:** the player's hand appears as a two-finger gripper, so they grab under a robot's constraints. It's a virtual cousin of UMI's handheld gripper, and it narrows the gap between human and robot data.
- **Live learning loop:** the robot learns next to the player in the same scene, its improvement is visible within one game, and it asks the player to teach it the items it's weakest on.

## 3. VR grasp demonstrations with data multiplication
- **SINTEF fish-grasping study (2017):** researchers demonstrated grasps in VR, then used domain randomization to turn a few dozen demonstrations into about 76,000 synthetic training grasps. Single object type, demonstrations from researchers.

**How we differ:** we use the same idea (VR demos plus domain randomization), but with many object types, demos from the public through a game, and the multiplication shown to the player as part of the experience.

## 4. Real recycling robots
- Recycling facilities already use AI sorting robots that learn from camera data collected in the facility, often from the robots' own pick attempts.
- Many sorting robots use suction cups rather than fingers.
- Lithium-ion batteries hidden in recycling are a known cause of fires at sorting facilities, but they're rare in any single facility's data.

**How SortQuest fits:** it's a way to bootstrap grasp data before real data exists and to generate many examples of rare, dangerous items safely. It complements real robot data rather than replacing it.

## 5. Honest limitations (say these before judges ask)
- **Sim-to-real gap:** virtual trash is cleaner and simpler than real, crumpled, dirty trash. We randomize object size, shape, and pose, and we treat game data as pre-training, not the final model. The standard recipe is to pre-train on cheap data and fine-tune on a little real data.
- **Suction grippers:** we record the pick point and approach direction, which also tells a suction gripper where to pick.
- **Noisy players:** we keep only grabs that landed in the correct bin without being dropped, and data from accurate players can be weighted more heavily.
- **Scale:** relatively few people own VR headsets. A booth, classroom, or museum setting is the near-term use.

## Suggested README pitch line
"Recent research shows VR gameplay data can help train real robots. SortQuest applies that idea to recycling, where robots face a data shortage for new facilities and for rare, dangerous items like lithium-ion batteries."
