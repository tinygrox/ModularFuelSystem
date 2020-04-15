using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections.ObjectModel;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
    // A FuelTank is a single TANK {} entry from the part.cfg file.
    // it defines four properties:
    // name         The name of the resource that can be stored.
    // utilization  How much of the tank is devoted to that resource (vs.
    //              how much is wasted in cryogenics or pumps).
    //              This is in resource units per volume unit.
    // mass         How much the part's mass is increased per volume unit
    //              of tank installed for this resource type. Tons per
    //              volume unit.
    // temperature  the part temperature at which this tank's contents start boiling
    // loss_rate    How quickly this resource type bleeds out of the tank. 
    //              (TODO: instead of this unrealistic static loss_rate, all 
    //              resources should have vsp (heat of vaporization) added and optionally conduction)
    //
    //

	public class FuelTank: IConfigNode
	{
		//------------------- fields
		[Persistent]
		public string name = "UnknownFuel";
		[Persistent]
		public string displayName = "";
		[Persistent]
		public string note = "";
      
        public string boiloffProduct = "";

        [Persistent]
		public float utilization = 1.0f;
		[Persistent]
		public float mass = 0.0f;
		[Persistent]
		public float cost = 0.0f;
        // TODO Retaining for fallback purposes but should be deprecated
		[Persistent]
		public double loss_rate = 0.0;

        public double vsp;

        public double resourceConductivity = 10;

        // cache for tank.totalArea and tank.tankRatio for use by ModuleFuelTanksRF
        public double totalArea = -1;
        public double tankRatio = -1;

        //[Persistent]
        public double wallThickness = 0.1;
        //[Persistent]
        public double wallConduction = 205; // Aluminum conductive factor (@cryogenic temperatures)
        //[Persistent]
        public double insulationThickness = 0.0;
        //[Persistent]
        public double insulationConduction = 1.0;
        public bool isDewar;

		[Persistent]
		public float temperature = 300.0f;
		[Persistent]
		public bool fillable = true;
        [Persistent]
        public string techRequired = "";

		public bool locked = false;

		public bool propagate = true;

        public double density = 0d;

        public bool resourceAvailable;

		internal string amountExpression;
		internal string maxAmountExpression;

		[NonSerialized]
		private ModuleFuelTanks module;


        public PartResourceDefinition boiloffProductResource;

		//------------------- virtual properties
		public Part part
		{
			get {
				if (module == null) {
					return null;
				}
				return module.part;
			}
		}

		public PartResource resource
		{
			get {
				if (part == null) {
					return null;
				}
				return part.Resources[name];
			}
		}
        /*
        public PartResourceDefinition boiloffProductResource
        {
            get
            {
                if (boiloffProduct != "")
                {
                    if (_boiloffProductResource == null)
                        _boiloffProductResource = PartResourceLibrary.Instance.GetDefinition(boiloffProduct);
                    return _boiloffProductResource;
                }
                else
                    return null;
            }
        }
*/
        public void RaiseResourceInitialChanged (Part part, PartResource resource, double amount)
		{
			var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
			data.Set<PartResource> ("resource", resource);
			data.Set<double> ("amount", amount);
			part.SendEvent ("OnResourceInitialChanged", data, 0);
		}

		public void RaiseResourceMaxChanged (Part part, PartResource resource, double amount)
		{
			var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
			data.Set<PartResource> ("resource", resource);
			data.Set<double> ("amount", amount);
			part.SendEvent ("OnResourceMaxChanged", data, 0);
		}

		public void RaiseResourceListChanged (Part part)
		{
			part.ResetSimulationResources ();
			part.SendEvent ("OnResourceListChanged", null, 0);
		}

		public double amount
		{
			get {
				if (module == null) {
					throw new InvalidOperationException ("Amount is not defined until instantiated in a tank");
				}

				if (resource == null) {
					return 0.0;
				}
				return resource.amount;
			}
			set {
				if (module == null) {
					throw new InvalidOperationException ("Amount is not defined until instantiated in a tank");
				}

				PartResource partResource = resource;
				if (partResource == null) {
					return;
				}

				if (value > partResource.maxAmount) {
					value = partResource.maxAmount;
				}

				if (value == partResource.amount) {
					return;
				}

                double unmanagedAmount = 0;
                module.unmanagedResources.TryGetValue(resource.resourceName, out ModuleFuelTanks.UnmanagedResource unmanagedResource);
                if (unmanagedResource != null)
                    unmanagedAmount = unmanagedResource.amount;

                amountExpression = null;

				partResource.amount = value + unmanagedAmount;
				if (HighLogic.LoadedSceneIsEditor) {
					module.RaiseResourceInitialChanged (partResource, amount + unmanagedAmount);
					if (propagate) {
						foreach (Part sym in part.symmetryCounterparts) {
							PartResource symResc = sym.Resources[name];
							symResc.amount = value + unmanagedAmount;
							RaiseResourceInitialChanged (sym, symResc, amount + unmanagedAmount);
						}
					}
				}
			}
		}

        public bool canHave
        {
            get {
                if (techRequired.Equals("") || HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
                    return true;
                return ResearchAndDevelopment.GetTechnologyState(techRequired) == RDTech.State.Available;
            }
        }

		void DeleteTank ()
		{
			PartResource partResource = resource;
			// Delete it
			//Debug.LogWarning ("[MFT] Deleting tank from API " + name);
			maxAmountExpression = null;
            ModuleFuelTanks.UnmanagedResource unmanagedResource = null;

            if (module.unmanagedResources != null)
                module.unmanagedResources.TryGetValue(partResource.resourceName, out unmanagedResource);

            if (unmanagedResource == null)
            {
                // there are no unmanaged resources of this type so, business as usual
                part.Resources.Remove(partResource);
                part.SimulationResources.Remove(partResource);
            }
            else if (part.Resources.Contains(partResource.resourceName))
            {
                // part has a quantity of this resource which are unmanaged by MFT
                part.Resources[partResource.resourceName].amount = unmanagedResource.amount;
                part.Resources[partResource.resourceName].maxAmount = unmanagedResource.maxAmount;
            }
            else
            {
                // probably shouldn't GET here since the part should already have this resource and we should always have left the unmanaged portion remaining.
                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("name", unmanagedResource.name);
                node.AddValue("amount", unmanagedResource.amount);
                node.AddValue("maxAmount", unmanagedResource.maxAmount);
                part.AddResource(node);
            }
			module.RaiseResourceListChanged ();
			//print ("Removed.");

			// Update symmetry counterparts.
			if (HighLogic.LoadedSceneIsEditor && propagate)
            {
				foreach (Part sym in part.symmetryCounterparts)
                {
                    if (unmanagedResource == null)
                    {
                        PartResource symResc = sym.Resources[name];
                        sym.Resources.Remove(symResc);
                        sym.SimulationResources.Remove(symResc);
                    }
                    else if (part.Resources.Contains(partResource.resourceName))
                    {
                        sym.Resources[partResource.resourceName].amount = unmanagedResource.amount;
                        sym.Resources[partResource.resourceName].maxAmount = unmanagedResource.maxAmount;
                    }
                    else
                    {
                        // probably shouldn't GET here since the part should already have this resource and we should always have left the unmanaged portion remaining.
                        ConfigNode node = new ConfigNode("RESOURCE");
                        node.AddValue("name", unmanagedResource.name);
                        node.AddValue("amount", unmanagedResource.amount);
                        node.AddValue("maxAmount", unmanagedResource.maxAmount);
                        sym.AddResource(node);
                    }
                    RaiseResourceListChanged(sym);
                }
            }
			//print ("Sym removed");
		}

		void UpdateTank (double value)
		{
			PartResource partResource = resource;

            ModuleFuelTanks.UnmanagedResource unmanagedResource = null;
            double unmanagedAmount = 0;
            double unmanagedMaxAmount = 0;

            if (module.unmanagedResources != null)
                module.unmanagedResources.TryGetValue(partResource.resourceName, out unmanagedResource);
            
            if (unmanagedResource != null)
            {
                unmanagedAmount = unmanagedResource.amount;
                unmanagedMaxAmount = unmanagedResource.maxAmount;
            }


            if (value > partResource.maxAmount)
            {
				// If expanding, modify it to be less than overfull
				double maxQty = (module.AvailableVolume * utilization) + partResource.maxAmount - unmanagedMaxAmount;
				if (maxQty < value)
                {
					value = maxQty;
				}
			}

			// Do nothing if unchanged
			if (value + unmanagedMaxAmount == partResource.maxAmount)
				return;

			//Debug.LogWarning ("[MFT] Updating tank from API " + name + " amount: " + value);
			maxAmountExpression = null;

			// Keep the same fill fraction
			double newAmount = value * fillFraction;

			partResource.maxAmount = value + unmanagedMaxAmount;
			module.RaiseResourceMaxChanged (partResource, value);
			//print ("Set new maxAmount");

			if (newAmount + unmanagedAmount != partResource.amount)
            {
				partResource.amount = newAmount + unmanagedAmount;
				module.RaiseResourceInitialChanged (partResource, newAmount);
			}

			// Update symmetry counterparts.
			if (HighLogic.LoadedSceneIsEditor && propagate)
            {
				foreach (Part sym in part.symmetryCounterparts)
                {
					PartResource symResc = sym.Resources[name];
					symResc.maxAmount = value + unmanagedMaxAmount;
					RaiseResourceMaxChanged (sym, symResc, value);

					if (newAmount != symResc.amount)
                    {
						symResc.amount = newAmount + unmanagedAmount;
						RaiseResourceInitialChanged (sym, symResc, newAmount);
					}
				}
			}

			//print ("Symmetry set");
		}

		void AddTank (double value)
		{
            //Debug.LogWarning ("[MFT] Adding tank from API " + name + " amount: " + value);
            // The following is for unmanaged resource; if such a resource is defined then we probably shouldn't be here....
            ModuleFuelTanks.UnmanagedResource unmanagedResource = null;
            double unmanagedAmount = 0;
            double unmanagedMaxAmount = 0;

            if (module != null && module.unmanagedResources != null)
                module.unmanagedResources.TryGetValue(name, out unmanagedResource);
            if (unmanagedResource != null)
            {
                unmanagedAmount = unmanagedResource.amount;
                unmanagedMaxAmount = unmanagedResource.maxAmount;
            }



			var resDef = PartResourceLibrary.Instance.GetDefinition (name);
            var res = part.Resources[name];
            if (res == null)
                res = new PartResource (part);
			res.resourceName = name;
			res.SetInfo (resDef);
			res.amount = value + unmanagedAmount;
			res.maxAmount = value + unmanagedMaxAmount;
			res._flowState = true;
			res.isTweakable = resDef.isTweakable;
			res.isVisible = resDef.isVisible;
			res.hideFlow = false;
			res._flowMode = PartResource.FlowMode.Both;
			part.Resources.dict.Add (resDef.id, res);
			//Debug.Log ($"[MFT] AddTank {res.resourceName} {res.amount} {res.maxAmount} {res.flowState} {res.isTweakable} {res.isVisible} {res.hideFlow} {res.flowMode}");

			module.RaiseResourceListChanged ();

            // Update symmetry counterparts.
            if (HighLogic.LoadedSceneIsEditor && propagate)
            {
                foreach (Part sym in part.symmetryCounterparts)
                {
                    sym.Resources.dict.Add(resDef.id, new PartResource(res));
                }
            }
			if (HighLogic.LoadedSceneIsEditor && propagate)
            {
				foreach (Part sym in part.symmetryCounterparts)
                {
                    sym.Resources.dict.Add(resDef.id, new PartResource(res));
                    RaiseResourceListChanged(sym);
                }
            }
		}

		public double maxAmount {
			get {
				if (module == null) {
					throw new InvalidOperationException ("Maxamount is not defined until instantiated in a tank");
				}

				if (resource == null) {
					return 0.0f;
				}
                double unmanagedMaxAmount = 0;
                module.unmanagedResources.TryGetValue(resource.resourceName, out ModuleFuelTanks.UnmanagedResource unmanagedResource);
                if (unmanagedResource != null)
                    unmanagedMaxAmount = unmanagedResource.maxAmount;
                return resource.maxAmount - unmanagedMaxAmount;
			}

			set {
				if (module == null) {
					throw new InvalidOperationException ("Maxamount is not defined until instantiated in a tank");
				}
				//print ("*RK* Setting maxAmount of tank " + name + " of part " + part.name + " to " + value);

				PartResource partResource = resource;
				if (partResource != null && value <= 0.0) {
					DeleteTank ();
				} else if (partResource != null) {
					UpdateTank (value);
				} else if (value > 0.0) {
					AddTank (value);
				}
                module.massDirty = true;
			}

		}

		public double fillFraction
		{
			get {
				return amount / maxAmount;
			}
			set {
				amount = value * maxAmount;
			}
		}


		//------------------- implicit type conversions
		public override string ToString ()
		{
			if (name == null)
			{
				return "NULL";
			}
			else if (displayName == "")
			{
				return name;
			}
			else
				return displayName;
		}

		//------------------- IConfigNode implementation
		public void Load (ConfigNode node)
		{
			if (!(node.name.Equals ("TANK") && node.HasValue ("name"))) {
				return;
			}

			ConfigNode.LoadObjectFromConfig (this, node);
			if (node.HasValue ("efficiency") && !node.HasValue ("utilization")) {
				float.TryParse (node.GetValue ("efficiency"), out utilization);
			}

			amountExpression = node.GetValue ("amount") ?? amountExpression;
			maxAmountExpression = node.GetValue ("maxAmount") ?? maxAmountExpression;

			resourceAvailable = PartResourceLibrary.Instance.GetDefinition (name) != null;
            MFSSettings.resourceVsps.TryGetValue(name, out vsp);
            MFSSettings.resourceConductivities.TryGetValue(name, out resourceConductivity);


            if (node.HasValue("wallThickness"))
                double.TryParse(node.GetValue("wallThickness"), out wallThickness);
            if (node.HasValue("wallConduction"))
                double.TryParse(node.GetValue("wallConduction"), out wallConduction);
            if (node.HasValue("insulationThickness"))
                double.TryParse(node.GetValue("insulationThickness"), out insulationThickness);
            if (node.HasValue("insulationConduction"))
                double.TryParse(node.GetValue("insulationConduction"), out insulationConduction);
            if (node.HasValue("boiloffProduct"))
                boiloffProductResource = PartResourceLibrary.Instance.GetDefinition(node.GetValue("boiloffProduct"));
            if (node.HasValue("isDewar"))
                bool.TryParse(node.GetValue("isDewar"), out isDewar);

            GetDensity();
		}

		public void Save (ConfigNode node)
		{
			if (name == null) {
				return;
			}
			ConfigNode.CreateConfigFromObject (this, node);

			if (module == null) {
				node.AddValue ("amount", amountExpression);
				node.AddValue ("maxAmount", maxAmountExpression);
			} else {
				node.AddValue ("amount", amount.ToString ("G17"));
				node.AddValue ("maxAmount", maxAmount.ToString ("G17"));
			}
		}

		internal void InitializeAmounts ()
		{
			if (module == null) {
				return;
			}

			double v;
			if (maxAmountExpression == null) {
				maxAmount = 0;
				amount = 0;
				return;
			}

			if (maxAmountExpression.Contains ("%") && double.TryParse (maxAmountExpression.Replace ("%", "").Trim (), out v)) {
				maxAmount = v * utilization * module.volume * 0.01; // NK
			} else if (double.TryParse (maxAmountExpression, out v)) {
				maxAmount = v;
			} else {
				Debug.LogError ("Unable to parse max amount expression: " + maxAmountExpression + " for tank " + name);
				maxAmount = 0;
				amount = 0;
				maxAmountExpression = null;
				return;
			}
			maxAmountExpression = null;

			if (amountExpression == null) {
				amount = maxAmount;
				return;
			}

			if (amountExpression.ToLowerInvariant().Equals ("full")) {
				amount = maxAmount;
			} else if (amountExpression.Contains ("%") && double.TryParse (amountExpression.Replace ("%", "").Trim (), out v)) {
				amount = v * maxAmount * 0.01;
			} else if (double.TryParse (amountExpression, out v)) {
				amount = v;
			} else {
				amount = maxAmount;
				Debug.LogError ("Unable to parse amount expression: " + amountExpression + " for tank " + name);
			}
			amountExpression = null;
		}

		//------------------- Constructor
		public FuelTank (ConfigNode node)
		{
			Load (node);
		}

		internal FuelTank CreateCopy (ModuleFuelTanks toModule, ConfigNode overNode, bool initializeAmounts)
		{
			FuelTank clone = (FuelTank)MemberwiseClone ();
			clone.module = toModule;

			if (overNode != null) {
				clone.Load (overNode);
			}
			if (initializeAmounts) {
				clone.InitializeAmounts ();
			} else {
				clone.amountExpression = clone.maxAmountExpression = null;
			}
            clone.GetDensity();
			return clone;
		}

        internal void GetDensity()
        {
            PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(name);
            if (d != null)
                density = d.density;
            else
                density = 0d;
        }
	}
}
