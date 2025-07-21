using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using LemonUI.Elements;
//using NAudio.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace GTACoOp
{
    public enum TrafficMode
    {
        None,
        Parked,
        All,
    }

    public class SyncPed
    {
        public long Host;
        public MaxMind.GeoIP2.Responses.CountryResponse geoIP;
        public Ped Character;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsInVehicle;
        public bool IsJumping;

        public bool IsInCover;
        public bool IsWalking;
        public bool IsInMeleeCombat;
        public bool IsSprinting;

        public int ModelHash;
        public int CurrentWeapon;
        public bool IsShooting;
        public bool IsAiming;
        public Vector3 AimCoords;
        public float Latency;
        public bool IsHornPressed;
        public Vehicle MainVehicle { get; set; }

        public int VehicleSeat;
        public int PedHealth;

        public int VehicleHealth;
        public int VehicleHash;
        public Quaternion VehicleRotation;
        public Vector3 VehicleVelocity;
        public int VehiclePrimaryColor;
        public int VehicleSecondaryColor;
        public string Name;
        public bool Siren;
        public bool IsEngineRunning;
        public bool IsInBurnout;
        public bool HighBeamsOn;
        public bool LightsOn;
        public VehicleLandingGearState LandingGear;
        public int Livery;

        public float Steering;
        public float WheelSpeed;
        public string Plate;
        public int RadioStation;
        private int _stopTime;
        private bool _lastBurnout;

        public float Speed
        {
            get { return _speed; }
            set
            {
                _lastSpeed = _speed;
                _speed = value;
            }
        }

        public bool IsParachuteOpen;
        public bool IsInParachuteFreeFall;

        public double AverageLatency
        {
            get { return _latencyAverager.Average(); }
        }

        public int LastUpdateReceived
        {
            get { return _lastUpdateReceived; }
            set
            {
                if (_lastUpdateReceived != 0)
                {
                    _latencyAverager.Enqueue(value - _lastUpdateReceived);
                    if (_latencyAverager.Count >= 10)
                        _latencyAverager.Dequeue();
                }

                _lastUpdateReceived = value;
            }
        }

        public Dictionary<int, int> VehicleMods
        {
            get { return _vehicleMods; }
            set
            {
                if (value == null) return;
                _vehicleMods = value;
            }
        }

        public Dictionary<int, int> PedProps
        {
            get { return _pedProps; }
            set
            {
                if (value == null) return;
                _pedProps = value;
            }
        }

        private Vector3 _carPosOnUpdate;
        private Vector3 _lastVehiclePos;
        public Vector3 VehiclePosition
        {
            get { return _vehiclePosition; }
            set
            {
                _lastVehiclePos = _vehiclePosition;
                _vehiclePosition = value;
            }
        }

        public bool Blip
        {
            set
            {
                _blip = value;

                if (_mainBlip != null && !_blip)
                {
                    _mainBlip.Delete();
                    _mainBlip = null;
                }

                if (Character?.AttachedBlip != null && !_blip)
                {
                    Character.AttachedBlip.Delete();
                }

                // don't have to create the blip here, update code should take care of that
            }
        }

        private bool _lastVehicle;
        private uint _switch;
        private bool _lastAiming;
        private float _lastSpeed;
        private DateTime _secondToLastUpdateReceived;
        private bool _lastShooting;
        private bool _lastJumping;

        private bool _lastInCover;
        private bool _lastWalking;
        private bool _lastMeleeCombat;
        private bool _lastSprinting;


        private bool _blip;
        private bool _justEnteredVeh;
        private DateTime _lastHornPress = DateTime.Now;
        private RelationshipGroup _relGroup;
        private DateTime _enterVehicleStarted;
        private Vector3 _vehiclePosition;
        private Dictionary<int, int> _vehicleMods;
        private Dictionary<int, int> _pedProps;

        private Queue<double> _latencyAverager;

        private int _playerSeat;
        private bool _isStreamedIn;
        private Blip _mainBlip;
        private bool _lastHorn;
        private Prop _parachuteProp;

        public SyncPed(int hash, Vector3 pos, Quaternion rot, bool blip = true)
        {
            Position = pos;
            Rotation = rot;
            ModelHash = hash;
            _blip = blip;

            _latencyAverager = new Queue<double>();

            _relGroup = World.AddRelationshipGroup("SYNCPED");
            Game.Player.Character.RelationshipGroup.SetRelationshipBetweenGroups(_relGroup, Relationship.Neutral, true);
        }

        public void SetBlipName(Blip blip, string text)
        {
            Function.Call(Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
            Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, blip);
        }

        private int _lastUpdateReceived;
        private float _speed;


        public void DisplayLocally()
        {
            try
            {
                var isPlane = Function.Call<bool>(Hash.IS_THIS_MODEL_A_PLANE, VehicleHash);
                float hRange = isPlane ? 1200f : 400f;

                var gPos = IsInVehicle ? VehiclePosition : Position;
                var inRange = isPlane ? true : Game.Player.Character.IsInRange(gPos, hRange);

                if (inRange && !_isStreamedIn)
                {
                    _isStreamedIn = true;
                    if (_mainBlip != null)
                    {
                        _mainBlip.Delete();
                        _mainBlip = null;
                    }
                }
                else if(!inRange && _isStreamedIn)
                {
                    Clear();
                    _isStreamedIn = false;
                }

                if (!inRange)
                {
                    if (_mainBlip == null && _blip)
                    {
                        _mainBlip = World.CreateBlip(gPos);
                        _mainBlip.Color = BlipColor.White;
                        _mainBlip.Scale = 0.8f;
                        SetBlipName(_mainBlip, Name == null ? "<nameless>" : Name);
                    }
                    if(_blip && _mainBlip != null)
                        _mainBlip.Position = gPos;
                    return;
                }


                if (Character == null || !Character.Exists() || (!Character.IsInRange(gPos, hRange) && Game.GameTime - LastUpdateReceived < 5000) || Character.Model.Hash != ModelHash || (Character.IsDead && PedHealth > 0))
                {
                    if (Character != null) Character.Delete();

                    Character = World.CreatePed(new Model(ModelHash), gPos, Rotation.Z);
                    if (Character == null) return;

                    Character.BlockPermanentEvents = true;
                    Character.IsInvincible = true;
                    Character.CanRagdoll = false;
                    Character.RelationshipGroup = _relGroup;
                    if (_blip)
                    {
                        Character.AddBlip();
                        if (Character.AttachedBlip == null) return;
                        Character.AttachedBlip.Color = BlipColor.White;
                        Character.AttachedBlip.Scale = 0.8f;
                        SetBlipName(Character.AttachedBlip, Name);
                    }

                    if (PedProps != null)
                        for (int i = 0; i < 12; i++)
                            if (PedProps.ContainsKey(i))
                                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, Character.Handle, i, PedProps[i],
                                    PedProps.ContainsKey(i + 12) ? PedProps[i + 12] : 0,
                                    PedProps.ContainsKey(i + 24) ? PedProps[i + 24] : 0);

                    return;
                }

                if (!Character.IsOccluded && Character.IsInRange(Game.Player.Character.Position, 20f))
                {
                    Vector3 targetPos = Character.Bones[Bone.IKHead].Position + new Vector3(0, 0, 0.5f);

                    targetPos += Character.Velocity / Game.FPS;

                    Function.Call(Hash.SET_DRAW_ORIGIN, targetPos.X, targetPos.Y, targetPos.Z, 0);

                    float sizeOffset = Math.Max(1f - ((GameplayCamera.Position - Character.Position).Length() / 30f), 0.3f);

                    new ScaledText(new Point(0, 0), Name ?? "<Nameless>", 0.4f * sizeOffset, GTA.UI.Font.ChaletLondon)
                    {
                        Outline = true,
                        Alignment = GTA.UI.Alignment.Center,
                        Color = Color.WhiteSmoke
                    }.Draw();

                    Function.Call(Hash.CLEAR_DRAW_ORIGIN);
                }

                if ((!_lastVehicle && IsInVehicle && VehicleHash != 0) || (_lastVehicle && IsInVehicle && (MainVehicle == null || !Character.IsInVehicle(MainVehicle) || MainVehicle.Model.Hash != VehicleHash || VehicleSeat != Util.GetPedSeat(Character))))
                {
                    if (MainVehicle != null && Util.IsVehicleEmpty(MainVehicle))
                    {
                        MainVehicle.MarkAsNoLongerNeeded();
                        MainVehicle.Delete();
                    }

                    var model = new Model(VehicleHash);
                    var veh = World.GetClosestVehicle(Character.Position, 3f, model);
                    if (veh != null)
                    {
                        MainVehicle = veh;
                        /*if (Game.Player.Character.IsInVehicle(MainVehicle) &&
                            VehicleSeat == Util.GetPedSeat(Game.Player.Character))
                        {
                            Game.Player.Character.Task.WarpOutOfVehicle(MainVehicle);
                            UI.Notify("~r~Car jacked!");
                        }*/
                    }
                    else
                    {
                        MainVehicle = World.CreateVehicle(model, gPos);
                    }

                    if (MainVehicle != null)
                    {
                        Function.Call(Hash.SET_VEHICLE_COLOURS, MainVehicle, VehiclePrimaryColor, VehicleSecondaryColor);
                        MainVehicle.Mods.Livery = Livery;

                        MainVehicle.Quaternion = VehicleRotation;
                        MainVehicle.IsInvincible = true;
                        //Character.Task.PlayAnimation("random@peyote@bird", "wakeup");
                        //Character.Task.EnterVehicle(MainVehicle, (VehicleSeat)VehicleSeat);

                        //Character.Task.PlayAnimation("random@peyote@bird", "wakeup");


                        //if (Function.Call<bool>(Hash.TASK_ENTER_VEHICLE, Character, "random@peyote@bird", "wakeup") )
                        //{
                        //    Function.Call(Hash.TASK_ENTER_VEHICLE, Character, MainVehicle, 1, -1, 1, 16, 0);
                        //    Notification.Show("Test is running anim");
                        //}

                        //Function.Call(Hash.TASK_ENTER_VEHICLE, Character, MainVehicle,1, -1, 1, 16, 0);


                        Character.SetIntoVehicle(MainVehicle, (VehicleSeat)VehicleSeat);


                        /*if (_playerSeat != -2 && !Game.Player.Character.IsInVehicle(_mainVehicle))
                        { // TODO: Fix me.
                            Game.Player.Character.Task.WarpIntoVehicle(_mainVehicle, (VehicleSeat)_playerSeat);
                        }*/
                    }

                    _lastVehicle = true;
                    _justEnteredVeh = true;
                    _enterVehicleStarted = DateTime.Now;
                    return;
                }

                if (_lastVehicle && _justEnteredVeh && IsInVehicle && !Character.IsInVehicle(MainVehicle) && DateTime.Now.Subtract(_enterVehicleStarted).TotalSeconds <= 4)
                {
                    return;
                }
                _justEnteredVeh = false;

                if (_lastVehicle && !IsInVehicle && MainVehicle != null)
                {
                    if (Character != null) Character.Task.LeaveVehicle(MainVehicle, true);
                }

                Character.Health = PedHealth;

                _switch++;

                if (!inRange)
                {
                    if (Character != null && Game.GameTime - LastUpdateReceived < 10000)
                    {
                        if (!IsInVehicle)
                        {
                            Character.PositionNoOffset = gPos;
                        }
                        else
                        {
                            var responsible = GetResponsiblePed(MainVehicle);
                            if (responsible != null && responsible.Handle == Character.Handle)
                            {
                                MainVehicle.Position = VehiclePosition;
                                MainVehicle.Quaternion = VehicleRotation;
                            }
                        }
                    }
                    return;
                }

                if (IsInVehicle)
                {
                    var responsible = GetResponsiblePed(MainVehicle);
                    if (responsible != null && responsible.Handle == Character.Handle)
                    {
                        MainVehicle.Health = VehicleHealth;
                        if (MainVehicle.Health <= 0)
                        {
                            MainVehicle.IsInvincible = false;
                            //_mainVehicle.Explode();
                        }
                        else
                        {
                            MainVehicle.IsInvincible = true;
                            if (MainVehicle.IsDead)
                                MainVehicle.Repair();
                        }

                        MainVehicle.IsEngineRunning = IsEngineRunning;

                        if (Plate != null)
                        {
                            MainVehicle.Mods.LicensePlate = Plate;
                        }

                        var radioStations = Util.GetRadioStations();

                        if (radioStations?.ElementAtOrDefault(RadioStation) != null)
                        {
                            Function.Call(Hash.SET_VEH_RADIO_STATION, radioStations[RadioStation]);
                        }

                        if (VehicleMods != null && Game.Player.Character.IsInRange(VehiclePosition, 30f))
                        {
                            foreach (KeyValuePair<int, int> mod in VehicleMods)
                            {
                                if (MainVehicle.Mods[(VehicleModType)mod.Key].Index != mod.Value)
                                {
                                    Function.Call(Hash.SET_VEHICLE_MOD_KIT, MainVehicle.Handle, 0);
                                    MainVehicle.Mods[(VehicleModType)mod.Key].Index = mod.Value;
                                    Function.Call(Hash.RELEASE_PRELOAD_MODS, mod.Key);
                                }
                            }
                        }

                        if (IsHornPressed && !_lastHorn)
                        {
                            _lastHorn = true;
                            MainVehicle.SoundHorn(99999);
                        }

                        if (!IsHornPressed && _lastHorn)
                        {
                            _lastHorn = false;
                            MainVehicle.SoundHorn(1);
                        }

                        if (IsInBurnout && !_lastBurnout)
                        {
                            Function.Call(Hash.SET_VEHICLE_BURNOUT, MainVehicle, true);
                            Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, Character, MainVehicle, 23, 120000); // 30 - burnout
                        }
                        else if (!IsInBurnout && _lastBurnout)
                        {
                            Function.Call(Hash.SET_VEHICLE_BURNOUT, MainVehicle, false);
                            Character.Task.ClearAll();
                        }

                        _lastBurnout = IsInBurnout;

                        Function.Call(Hash.SET_VEHICLE_BRAKE_LIGHTS, MainVehicle, Speed > 0.2 && _lastSpeed > Speed);

                        if (MainVehicle.IsSirenActive && !Siren)
                            MainVehicle.IsSirenActive = Siren;
                        else if (!MainVehicle.IsSirenActive && Siren)
                            MainVehicle.IsSirenActive = Siren;

                        MainVehicle.AreLightsOn = LightsOn;
                        MainVehicle.AreHighBeamsOn = HighBeamsOn;
                        MainVehicle.IsSirenActive = Siren;
                        MainVehicle.SteeringAngle = (Steering > 5f || Steering < -5f) ? Steering : 0f;
                        Function.Call(Hash.SET_VEHICLE_LIVERY, MainVehicle, Livery);

                        Function.Call(Hash.SET_VEHICLE_COLOURS, MainVehicle, VehiclePrimaryColor, VehicleSecondaryColor);

                        if (MainVehicle.Model.IsPlane && LandingGear != MainVehicle.LandingGearState)
                        {
                            MainVehicle.LandingGearState = LandingGear;
                        }

                        if (Character.IsOnBike && MainVehicle.ClassType == VehicleClass.Cycles)
                        {
                            var isPedaling = IsPedaling(false);
                            var isFastPedaling = IsPedaling(true);
                            if (Speed < 2f)
                            {
                                if (isPedaling)
                                    StopPedalingAnim(false);
                                else if (isFastPedaling)
                                    StopPedalingAnim(true);
                            }
                            else if (Speed < 11f && !isPedaling)
                                StartPedalingAnim(false);
                            else if (Speed >= 11f && !isFastPedaling)
                                StartPedalingAnim(true);
                        }

                        if ((Speed > 0.2f || IsInBurnout) && MainVehicle.IsInRange(VehiclePosition, 7.0f))
                        {
                            MainVehicle.Velocity = VehicleVelocity + (VehiclePosition - MainVehicle.Position);

                            MainVehicle.Quaternion = Quaternion.Slerp(MainVehicle.Quaternion, VehicleRotation, 0.5f);

                            _stopTime = Game.GameTime;
                        }
                        else if ((Game.GameTime - _stopTime) <= 1000)
                        {
                            Vector3 posTarget = Util.LinearVectorLerp(MainVehicle.Position, VehiclePosition + (VehiclePosition - MainVehicle.Position), (Game.GameTime - _stopTime), 1000);
                            MainVehicle.PositionNoOffset = posTarget;
                            MainVehicle.Quaternion = Quaternion.Slerp(MainVehicle.Quaternion, VehicleRotation, 0.5f);
                        }
                        else
                        {
                            MainVehicle.PositionNoOffset = VehiclePosition;
                            MainVehicle.Quaternion = VehicleRotation;
                        }
                    }
                }
                else
                {
                    if (PedProps != null && Game.Player.Character.IsInRange(Position, 30f))
                        for (int i = 0; i < 12; i++)
                            if (PedProps.ContainsKey(i) && PedProps[i] != Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, Character.Handle, i))
                                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, Character.Handle, i, PedProps[i],
                                    PedProps.ContainsKey(i + 12) ? PedProps[i + 12] : 0,
                                    PedProps.ContainsKey(i + 24) ? PedProps[i + 24] : 0);

                    if (Character.Weapons.Current.Hash != (WeaponHash) CurrentWeapon)
                    {
                        var wep = Character.Weapons.Give((WeaponHash) CurrentWeapon, 9999, true, true);
                        Character.Weapons.Select(wep);
                    }

                    if (!_lastJumping && IsJumping)
                    {
                        Character.Task.Jump();
                        //Character.Task.HandsUp(300);
                        Notification.Show("Player jumping");
                    }

                    if (!_lastInCover && IsInCover)
                    {
                        //Character.Task.HandsUp(300);
                        Character.Task.PlayAnimation("cover@move@base@core", "low_idle_l", 5.0f, 5.0f, -1, AnimationFlags.Loop, 1.0f);

                        Notification.Show("Player in Cover && not moving");

                        //if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, Character, "cover@move@base@core", "low_idle_l", 3) == false)
                        //{
                        //    Character.Task.PlayAnimation("cover@move@base@core", "low_idle_l");
                        //    Notification.Show("Player in Cover");
                        //}


                    }

                    //if (IsInMeleeCombat && !IsWalking & !IsSprinting)
                    //{
                    //    //Character.Task.PlayAnimation("melee@unarmed@streamed_core", "kick_close_a", 8.0f, 8.0f, 1000, AnimationFlags.None, 0.0f);
                    //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "heavy_punch_a", 0.0f, -0.25f, -1, AnimationFlags.None, 0.0f);

                    //    Notification.Show("Player IsInMeleeCombat not moving");
                    //}


                    //if (!_lastInCover && IsInCover & IsWalking)
                    //{
                    //    //Character.Task.HandsUp(300);
                    //    Character.Task.PlayAnimation("cover@move@base@core", "low_l_walkstart", 8.0f, -8.0f, -1, AnimationFlags.None, 0.0f);

                    //    Notification.Show("Player in Cover, moving");


                    //}





                    //if (IsInMeleeCombat && IsWalking)
                    //{
                    //    //Character.Task.PlayAnimation("melee@unarmed@streamed_core", "walking_punch", 0.0f, 0.0f, -1, AnimationFlags.None, 0.0f);
                    //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "heavy_punch_a", 0.0f, -0.25f, -1, AnimationFlags.None, 0.0f);

                    //    Notification.Show("Player IsInMeleeCombat && walking");
                    //}

                    //if (IsWalking)
                    //{
                    //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "walking_punch", 0.0f, 0.0f, -1, AnimationFlags.None, 0.0f);
                    //    Notification.Show("Player walking");
                    //}

                    //if (IsSprinting)
                    //{
                    //    //Character.Task.PlayAnimation("melee@unarmed@streamed_core", "walking_punch", 0.0f, 0.0f, -1, AnimationFlags.None, 0.0f);
                    //    //Notification.Show("Player sprinting");

                    //    if (IsInMeleeCombat)
                    //    {
                    //        Character.Task.PlayAnimation("melee@unarmed@streamed_core", "walking_punch", 0.0f, 0.0f, -1, AnimationFlags.None, 0.0f);
                    //        Notification.Show("Player sprinting && melee");
                    //    }
                    //}




                    //if (IsSprinting && IsInMeleeCombat && Character.IsInRange(Position, 0.5f))
                    //{
                    //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "walking_punch", 0.0f, 0.0f, -1, AnimationFlags.None, 0.0f);
                    //    Notification.Show("Player IsSprinting");
                    //}

                    //if (IsWalking && IsInMeleeCombat && Character.IsInRange(Position, 0.5f))
                    //{
                    //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "walking_punch", 0.0f, 0.0f, -1, AnimationFlags.None, 0.0f);
                    //    Notification.Show("Player IsSprinting");
                    //}



                    if (IsParachuteOpen)
                    {
                        if (_parachuteProp == null)
                        {
                            _parachuteProp = World.CreateProp(new Model(1740193300), Character.Position,
                                Character.Rotation, false, false);
                            _parachuteProp.IsPositionFrozen = true;
                            Function.Call(Hash.SET_ENTITY_COLLISION, _parachuteProp.Handle, false, 0);
                        }
                        Character.IsPositionFrozen = true;
                        Character.Position = Position - new Vector3(0, 0, 1);
                        Character.Quaternion = Rotation;
                        _parachuteProp.Position = Character.Position + new Vector3(0, 0, 3.7f);
                        _parachuteProp.Quaternion = Character.Quaternion;

                        Character.Task.PlayAnimation("skydive@parachute@first_person", "chute_idle_right", 8f, -8f, 5000,
                            AnimationFlags.None, 0f);
                    }
                    else
                    {
                        var dest = Position;
                        Character.IsPositionFrozen = false;

                        if (_parachuteProp != null)
                        {
                            _parachuteProp.Delete();
                            _parachuteProp = null;
                        }


                        const int threshold = 50;
                        if (IsAiming && !IsShooting && !Character.IsInRange(Position, 0.5f) && _switch%threshold == 0)
                        {
                            Function.Call(Hash.TASK_GO_TO_COORD_WHILE_AIMING_AT_COORD, Character.Handle, dest.X, dest.Y,
                                dest.Z, AimCoords.X, AimCoords.Y, AimCoords.Z, 2f, 0, 0x3F000000, 0x40800000, 1, 512, 0,
                                (uint)FiringPattern.FullAuto);

                            //if (Character.Weapons.Current.Hash == WeaponHash.Unarmed)
                            //{
                            //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "kick_close_a");
                            //}
                            //else
                            //{
                            //    Function.Call(Hash.TASK_GO_TO_COORD_WHILE_AIMING_AT_COORD, Character.Handle, dest.X, dest.Y,
                            //    dest.Z, AimCoords.X, AimCoords.Y, AimCoords.Z, 2f, 0, 0x3F000000, 0x40800000, 1, 512, 0,
                            //    (uint)FiringPattern.FullAuto);
                            //}

                            //Notification.Show("Case 1");
                        }
                        //aiming not shooting in range
                        else if (IsAiming && !IsShooting && Character.IsInRange(Position, 0.5f))
                        {
                            Character.Task.AimAt(AimCoords, 100);

                            //Character.Task.PlayAnimation("random@peyote@bird", "wakeup");
                            //if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, Character, "melee@unarmed@streamed_core", "kick_close_a", 3) == true)
                            //{
                            //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "kick_close_a");
                            //}
                            //Character.Task.PlayAnimation("melee@unarmed@streamed_core", "kick_close_a");

                            //if (Character.Weapons.Current.Hash == WeaponHash.Unarmed)
                            //{
                            //    //Character.Task.PlayAnimation("melee@unarmed@streamed_core", "kick_close_a");
                            //    Character.Task.PlayAnimation("melee@unarmed@streamed_variations", "melee_intro_a");

                            //}
                            //else
                            //{
                            //    Character.Task.AimAt(AimCoords, 100);
                            //}
                            //Notification.Show("Case 2");


                        }
                        //not in range, shooting or is shooting switch?
                        if (!Character.IsInRange(Position, 0.5f) &&
                            ((IsShooting && !_lastShooting) ||
                             (IsShooting && _lastShooting && _switch%(threshold*2) == 0)))
                        {
                            Function.Call(Hash.TASK_GO_TO_COORD_WHILE_AIMING_AT_COORD, Character.Handle, dest.X, dest.Y,
                                dest.Z, AimCoords.X, AimCoords.Y, AimCoords.Z, 2f, 1, 0x3F000000, 0x40800000, 1, 0, 0,
                                (uint)FiringPattern.FullAuto);

                            //if (Character.Weapons.Current.Hash == WeaponHash.Unarmed)
                            //{
                            //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "kick_close_a");
                            //    Function.Call(Hash.TASK_GO_TO_COORD_WHILE_AIMING_AT_COORD, Character.Handle, dest.X, dest.Y,
                            //        dest.Z, AimCoords.X, AimCoords.Y, AimCoords.Z, 2f, 1, 0x3F000000, 0x40800000, 1, 0, 0,
                            //        (uint)FiringPattern.FullAuto);
                            //}
                            //else
                            //{
                            //    Function.Call(Hash.TASK_GO_TO_COORD_WHILE_AIMING_AT_COORD, Character.Handle, dest.X, dest.Y,
                            //        dest.Z, AimCoords.X, AimCoords.Y, AimCoords.Z, 2f, 1, 0x3F000000, 0x40800000, 1, 0, 0,
                            //        (uint)FiringPattern.FullAuto);
                            //}


                            Notification.Show("Case 3");
                        }
                        //is shooting?
                        else if ((IsShooting && !_lastShooting) ||
                                 (IsShooting && _lastShooting && _switch%(threshold/2) == 0))
                        {
                            Function.Call(Hash.TASK_SHOOT_AT_COORD, Character.Handle, AimCoords.X, AimCoords.Y,
                                AimCoords.Z, 1500, (uint)FiringPattern.FullAuto);

                            //if (Character.Weapons.Current.Hash == WeaponHash.Unarmed)
                            //{
                            //    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "kick_close_a");
                            //}
                            //else
                            //{
                            //    Function.Call(Hash.TASK_SHOOT_AT_COORD, Character.Handle, AimCoords.X, AimCoords.Y,
                            //    AimCoords.Z, 1500, (uint) FiringPattern.FullAuto);
                            //}
                            Notification.Show("Case 4");

                        }

                        if (!IsAiming && !IsShooting && !IsJumping && !IsInParachuteFreeFall)
                        {
                            float distance = Character.Position.DistanceTo(Position);
                            if (distance <= 0.15f || distance > 7.0f) // Still or too far away
                            {
                                if (distance > 7.0f)
                                {
                                    Character.Position = dest - new Vector3(0, 0, 1f);
                                    Character.Quaternion = Rotation;
                                }

                                if (IsInMeleeCombat && !IsWalking & !IsSprinting)
                                {
                                    //Character.Task.PlayAnimation("melee@unarmed@streamed_core", "kick_close_a", 8.0f, 8.0f, 1000, AnimationFlags.None, 0.0f);
                                    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "heavy_punch_a", 0.0f, -0.25f, -1, AnimationFlags.None, 0.0f);

                                    Notification.Show("Player IsInMeleeCombat not moving");
                                }
                            }
                            else if (distance <= 1.25f) // Walking
                            {
                                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, Character, Position.X, Position.Y, Position.Z, 1.0f, -1, Character.Heading, 0.0f);
                                Function.Call(Hash.SET_PED_DESIRED_MOVE_BLEND_RATIO, Character, 1.0f);

                            }
                            else if (distance > 1.75f) // Sprinting
                            {
                                if(!IsInMeleeCombat)
                                {
                                    Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, Character, Position.X, Position.Y, Position.Z, 3.0f, -1, Character.Heading, 2.0f);
                                    Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, Character, 1.49f);
                                    Function.Call(Hash.SET_PED_DESIRED_MOVE_BLEND_RATIO, Character, 3.0f);
                                }

                                if (IsInMeleeCombat)
                                {
                                    Character.Task.PlayAnimation("melee@unarmed@streamed_core", "walking_punch", 0.0f, -0.25f, -1, AnimationFlags.None, 0.0f);
                                    Notification.Show("Player sprinting && melee");
                                }
                            }
                            else // Running
                            {
                                Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, Character, Position.X, Position.Y, Position.Z, 4.0f, -1, Character.Heading, 1.0f);
                                Function.Call(Hash.SET_PED_DESIRED_MOVE_BLEND_RATIO, Character, 2.0f);
                            }
                            Notification.Show("Case 5");
                        }

                        if (IsInParachuteFreeFall)
                        {
                            if (!Function.Call<bool>(Hash.IS_PED_IN_PARACHUTE_FREE_FALL, Character))
                                Function.Call(Hash.TASK_SKY_DIVE, Character);
                            Character.Position = dest - new Vector3(0, 0, 1f);
                            Character.Quaternion = Rotation;
                        }
                    }
                    _lastJumping = IsJumping;
                    _lastShooting = IsShooting;
                    _lastAiming = IsAiming;

                    _lastInCover = IsInCover;
                    _lastWalking = IsWalking;
                    _lastMeleeCombat = IsInMeleeCombat;

                    _lastSprinting = IsSprinting;

                }
                _lastVehicle = IsInVehicle;
            }
            catch (Exception ex)
            {
                Notification.Show("Sync error: " + ex.Message);
                Main.Logger.WriteException("Exception in SyncPed code", ex);
            }
        }

        public static Ped GetResponsiblePed(Vehicle veh)
        {
            if (veh == null || veh.Handle == 0 || !veh.Exists()) return null;

            var driver = veh.GetPedOnSeat(GTA.VehicleSeat.Driver);
            if (driver != null) return driver;

            for (int i = 0; i < veh.PassengerCapacity; i++)
            {
                var passenger = veh.GetPedOnSeat((VehicleSeat)i);
                if (passenger != null) return passenger;
            }

            return null;
        }

        private string PedalingAnimDict()
        {
            string anim;
            switch ((VehicleHash)MainVehicle.Model.Hash)
            {
                case GTA.VehicleHash.Bmx:
                    anim = "veh@bicycle@bmx@front@base";
                    break;
                case GTA.VehicleHash.Cruiser:
                    anim = "veh@bicycle@cruiserfront@base";
                    break;
                case GTA.VehicleHash.Scorcher:
                    anim = "veh@bicycle@mountainfront@base";
                    break;
                default:
                    anim = "veh@bicycle@roadfront@base";
                    break;
            }
            return anim;
        }

        private string PedalingAnimName(bool fast)
        {
            return fast ? "fast_pedal_char" : "cruise_pedal_char";
        }

        private bool IsPedaling(bool fast)
        {
            return Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, Character.Handle, PedalingAnimDict(), PedalingAnimName(fast), 3);
        }

        private void StartPedalingAnim(bool fast)
        {
            Character.Task.PlayAnimation(PedalingAnimDict(), PedalingAnimName(fast), 8.0f, -8.0f, -1, AnimationFlags.Loop | AnimationFlags.Secondary, 5.0f);
        }

        private void StopPedalingAnim(bool fast)
        {
            Character.Task.ClearAnimation(PedalingAnimDict(), PedalingAnimName(fast));
        }

        public void Clear()
        {
            try
            {
                if (Character != null)
                {
                    Character.Model.MarkAsNoLongerNeeded();
                    Character.Delete();
                }
                if (_mainBlip != null)
                {
                    _mainBlip.Delete();
                    _mainBlip = null;
                }
                if (MainVehicle != null && Util.IsVehicleEmpty(MainVehicle))
                {
                    MainVehicle.Model.MarkAsNoLongerNeeded();
                    MainVehicle.Delete();
                }
                if (_parachuteProp != null)
                {
                    _parachuteProp.Delete();
                    _parachuteProp = null;
                }
            } catch (Exception ex)
            {
                Notification.Show("Clear sync error: " + ex.Message);
            }
        }
    }
}
