using System.Collections.Generic;
using UnityEngine;

namespace TheZooMustGrow
{
	public class HexMapGenerator : MonoBehaviour
	{
		public HexGrid grid;
		private int cellCount;
		private int landCells;

		public int seed;
		public bool useFixedSeed;

		private HexCellPriorityQueue searchFrontier;
		private int searchFrontierPhase;

		private List<MapRegion> regions;
		struct MapRegion
		{
			public int xMin, xMax, zMin, zMax;
		}

		private List<ClimateData> climate = new List<ClimateData>();
		private List<ClimateData> nextClimate = new List<ClimateData>();
		private List<HexDirection> flowDirections = new List<HexDirection>();

		struct ClimateData
        {
			public float clouds;
			public float moisture;
        }

		[Range(0f, 0.5f)]
		public float jitterProbability = 0.25f;

		[Range(20, 200)]
		public int chunkSizeMin = 30;

		[Range(20, 200)]
		public int chunkSizeMax = 100;

		[Range(5, 95)]
		public int landPercentage = 50;

		[Range(1, 5)]
		public int waterLevel = 3;

		[Range(0f, 1f)]
		public float highRiseProbability = 0.25f;

		[Range(0f, 0.4f)]
		public float sinkProbability = 0.2f;

		[Range(-4, 0)]
		public int elevationMinimum = -2;

		[Range(6, 10)]
		public int elevationMaximum = 8;

		[Range(0, 10)]
		public int mapBorderX = 5;

		[Range(0, 10)]
		public int mapBorderZ = 5;

		[Range(0, 10)]
		public int regionBorder = 5;

		[Range(1, 4)]
		public int regionCount = 1;

		[Range(0, 100)]
		public int erosionPercentage = 50;

		[Range(0f, 1f)]
		public float startingMoisture = 0.1f;

		[Range(0f, 1f)]
		public float evaporationFactor = 0.5f;

		[Range(0f, 1f)]
		public float precipitationFactor = 0.25f;

		[Range(0f, 1f)]
		public float runoffFactor = 0.25f;

		[Range(0f, 1f)]
		public float seepageFactor = 0.125f;

		public HexDirection windDirection = HexDirection.NW;
		[Range(1f, 10f)]
		public float windStrength = 4f;

		[Range(0, 20)]
		public int riverPercentage = 10;

		[Range(0f, 1f)]
		public float extraLakeProbability = 0.25f;

		public void GenerateMap(int x, int z)
		{
			Random.State originalRandomState = Random.state;

			if (!useFixedSeed)
			{
				seed = Random.Range(0, int.MaxValue);
				seed ^= (int)System.DateTime.Now.Ticks;
				seed ^= (int)Time.unscaledTime;
				seed &= int.MaxValue;
			}
			Random.InitState(seed);

			cellCount = x * z;

			// Generate base map
			grid.CreateMap(x, z);

            // Initiate search frontier
            if (searchFrontier == null)
            {
				searchFrontier = new HexCellPriorityQueue();
            }

            // Set base water level
            for (int i = 0; i < cellCount; i++)
            {
				grid.GetCell(i).WaterLevel = waterLevel;
            }

			// Set land bound constraints
			CreateRegions();

			CreateLand();
			ErodeLand();
			CreateClimate();
			CreateRivers();
			SetTerrainType();

            // Set all search phase variables in cells to zero
            for (int i = 0; i < cellCount; i++)
            {
				grid.GetCell(i).SearchPhase = 0;
            }

			Random.state = originalRandomState;
		}

		private void CreateLand()
        {
            int landBudget = Mathf.RoundToInt(cellCount * landPercentage * 0.01f);
			landCells = landBudget;

			for (int guard = 0; guard < 10000; guard++)
			{
				bool sink = Random.value < sinkProbability;

				// Loop through regions
				for (int i = 0; i < regions.Count; i++)
				{
					MapRegion region = regions[i];

					int chunkSize = Random.Range(chunkSizeMin, chunkSizeMax + 1);

					// Determine to raise or sink land
					if (sink)
					{
						landBudget = SinkTerrain(chunkSize, landBudget, region);
					}
					else
					{
						landBudget = RaiseTerrain(chunkSize, landBudget, region);
						
						// Check if landBudget is met
						if (landBudget == 0)
                        {
							return;
                        }
					}
				}
			}

			// Check if the landbudget was used,
			// or the map creation timed out using the guard
			if (landBudget > 0)
            {
				Debug.LogWarning("Failed to use up " + landBudget + "land budget.");
				landCells -= landBudget;
            }
        }

		private int RaiseTerrain(int chunkSize, int budget, MapRegion region)
        {
			// Start search for random group of terrain
			searchFrontierPhase += 1;
			HexCell firstCell = GetRandomCell(region);
			firstCell.SearchPhase = searchFrontierPhase;
			firstCell.Distance = 0;
			firstCell.SearchHeuristic = 0;
			searchFrontier.Enqueue(firstCell);

			HexCoordinates center = firstCell.coordinates;

			int rise = Random.value < highRiseProbability ? 2 : 1;
			int size = 0;
			while (size < chunkSize && searchFrontier.Count > 0)
            {
				HexCell current = searchFrontier.Dequeue();

				int originalElevation = current.Elevation;

				int newElevation = originalElevation + rise;
				if (newElevation > elevationMaximum)
                {
					continue;
                }

				current.Elevation = newElevation;

				// If the current cell has been raised passed the water level and check budget
				if (originalElevation < waterLevel &&
					newElevation >= waterLevel 
					&& --budget == 0)
                {
					break;
                }
				size += 1;

				for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
				{
					HexCell neighbor = current.GetNeighbor(d);
					if(neighbor && neighbor.SearchPhase < searchFrontierPhase)
                    {
						neighbor.SearchPhase = searchFrontierPhase;
						neighbor.Distance = neighbor.coordinates.DistanceTo(center);
						neighbor.SearchHeuristic = Random.value < jitterProbability ? 1 : 0;
						searchFrontier.Enqueue(neighbor);
                    }

				}
            }
			searchFrontier.Clear();
			return budget;
        }

		private int SinkTerrain(int chunkSize, int budget, MapRegion region)
		{
			// Start search for random group of terrain
			searchFrontierPhase += 1;
			HexCell firstCell = GetRandomCell(region);
			firstCell.SearchPhase = searchFrontierPhase;
			firstCell.Distance = 0;
			firstCell.SearchHeuristic = 0;
			searchFrontier.Enqueue(firstCell);

			HexCoordinates center = firstCell.coordinates;

			int sink = Random.value < highRiseProbability ? 2 : 1;
			int size = 0;
			while (size < chunkSize && searchFrontier.Count > 0)
			{
				HexCell current = searchFrontier.Dequeue();
				int originalElevation = current.Elevation;

				int newElevation = current.Elevation - sink;
				if (newElevation < elevationMinimum)
                {
					continue;
                }

				current.Elevation = newElevation;

				// If the current cell has been lowered passed the water level
				if (originalElevation >= waterLevel &&
					newElevation < waterLevel
					&& --budget == 0)
				{
					budget += 1;
				}
				size += 1;

				for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
				{
					HexCell neighbor = current.GetNeighbor(d);
					if (neighbor && neighbor.SearchPhase < searchFrontierPhase)
					{
						neighbor.SearchPhase = searchFrontierPhase;
						neighbor.Distance = neighbor.coordinates.DistanceTo(center);
						neighbor.SearchHeuristic = Random.value < jitterProbability ? 1 : 0;
						searchFrontier.Enqueue(neighbor);
					}

				}
			}
			searchFrontier.Clear();
			return budget;
		}

		private void SetTerrainType()
		{
			for (int i = 0; i < cellCount; i++)
			{
				HexCell cell = grid.GetCell(i);
				float moisture = climate[i].moisture;
				if (!cell.IsUnderwater)
				{
					if (moisture < 0.05f)
					{
						cell.TerrainTypeIndex = 4;
					}
					else if (moisture < 0.12f)
					{
						cell.TerrainTypeIndex = 0;
					}
					else if (moisture < 0.28f)
					{
						cell.TerrainTypeIndex = 3;
					}
					else if (moisture < 0.85f)
					{
						cell.TerrainTypeIndex = 1;
					}
					else
					{
						cell.TerrainTypeIndex = 2;
					}
				}
				else
				{
					cell.TerrainTypeIndex = 2;
				}
			}
		}

		private HexCell GetRandomCell(MapRegion region)
        {
			return grid.GetCell(
				Random.Range(region.xMin, region.xMax),
				Random.Range(region.zMin, region.zMax));
		}

		private void CreateRegions()
        {
			if (regions == null)
            {
				regions = new List<MapRegion>();
            }
            else
            {
				regions.Clear();
            }

			MapRegion region;
			switch (regionCount)
			{
				default:
					region.xMin = mapBorderX;
					region.xMax = grid.cellCountX - mapBorderX;
					region.zMin = mapBorderZ;
					region.zMax = grid.cellCountZ - mapBorderZ;
					regions.Add(region);
					break;
				case 2:
					if (Random.value < 0.5f)
					{
						region.xMin = mapBorderX;
						region.xMax = grid.cellCountX / 2 - regionBorder;
						region.zMin = mapBorderZ;
						region.zMax = grid.cellCountZ - mapBorderZ;
						regions.Add(region);
						region.xMin = grid.cellCountX / 2 + regionBorder;
						region.xMax = grid.cellCountX - mapBorderX;
						regions.Add(region);
					}
					else
					{
						region.xMin = mapBorderX;
						region.xMax = grid.cellCountX - mapBorderX;
						region.zMin = mapBorderZ;
						region.zMax = grid.cellCountZ / 2 - regionBorder;
						regions.Add(region);
						region.zMin = grid.cellCountZ / 2 + regionBorder;
						region.zMax = grid.cellCountZ - mapBorderZ;
						regions.Add(region);
					}
					break;
				case 3:
					region.xMin = mapBorderX;
					region.xMax = grid.cellCountX / 3 - regionBorder;
					region.zMin = mapBorderZ;
					region.zMax = grid.cellCountZ - mapBorderZ;
					regions.Add(region);
					region.xMin = grid.cellCountX / 3 + regionBorder;
					region.xMax = grid.cellCountX * 2 / 3 - regionBorder;
					regions.Add(region);
					region.xMin = grid.cellCountX * 2 / 3 + regionBorder;
					region.xMax = grid.cellCountX - mapBorderX;
					regions.Add(region);
					break;
				case 4:
					region.xMin = mapBorderX;
					region.xMax = grid.cellCountX / 2 - regionBorder;
					region.zMin = mapBorderZ;
					region.zMax = grid.cellCountZ / 2 - regionBorder;
					regions.Add(region);
					region.xMin = grid.cellCountX / 2 + regionBorder;
					region.xMax = grid.cellCountX - mapBorderX;
					regions.Add(region);
					region.zMin = grid.cellCountZ / 2 + regionBorder;
					region.zMax = grid.cellCountZ - mapBorderZ;
					regions.Add(region);
					region.xMin = mapBorderX;
					region.xMax = grid.cellCountX / 2 - regionBorder;
					regions.Add(region);
					break;
			}
		}

		private void ErodeLand()
        {
			List<HexCell> erodibleCells = ListPool<HexCell>.Get();
            for (int i = 0; i < cellCount; i++)
            {
				HexCell cell = grid.GetCell(i);
				if (IsErodible(cell))
                {
					erodibleCells.Add(cell);
                }
            }

			// Determine the number to leave as erodible
			int targetErodibleCount = 
				(int)(erodibleCells.Count * (100 - erosionPercentage) * 0.01f);

			while (erodibleCells.Count > targetErodibleCount)
			{
				// Get random erodible cell to erode
				int index = Random.Range(0, erodibleCells.Count);

				HexCell cell = erodibleCells[index];
				HexCell targetCell = GetErosionTarget(cell);

				// Move elevation from erodible cell to erosion target
				cell.Elevation -= 1;
				targetCell.Elevation += 1;

				// Remove the cell if it is no longer erodible
				if (IsErodible(cell) == false)
				{
					erodibleCells[index] = erodibleCells[erodibleCells.Count - 1];
					erodibleCells.RemoveAt(erodibleCells.Count - 1);
				}

				// Check the cell's neighbors to see if they became erodible
				for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
				{
					HexCell neighbor = cell.GetNeighbor(d);
					if (neighbor && neighbor.Elevation == cell.Elevation + 2 &&
						!erodibleCells.Contains(neighbor))
					{
						erodibleCells.Add(neighbor);
					}
				}

				if (IsErodible(targetCell) && !erodibleCells.Contains(targetCell))
				{
					erodibleCells.Add(targetCell);
				}

				// Check if the erosion target's neighbors became erodible or not erodible
				for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
				{
					HexCell neighbor = targetCell.GetNeighbor(d);
					if (neighbor && neighbor != cell &&
						neighbor.Elevation == targetCell.Elevation + 1 &&
						!IsErodible(neighbor))
					{
						erodibleCells.Remove(neighbor);
					}
				}

			}

			// Re-add the temp list to the pool
			ListPool<HexCell>.Add(erodibleCells);
        }

		/// <summary>
		/// Returns true if a cell is high enough compared to neighbors to be erodible.
		/// </summary>
		/// <param name="cell"></param>
		/// <returns></returns>
		private bool IsErodible(HexCell cell)
		{
			int erodibleElevation = cell.Elevation - 2;
			for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
			{
				HexCell neighbor = cell.GetNeighbor(d);
				if (neighbor && neighbor.Elevation <= erodibleElevation)
				{
					return true;
				}
			}
			return false;
		}

		private HexCell GetErosionTarget(HexCell cell)
        {
			// Get list from list pool
			List<HexCell> candidates = ListPool<HexCell>.Get();
			
			int erodibleElevation = cell.Elevation - 2;
			for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
			{
				HexCell neighbor = cell.GetNeighbor(d);
				if (neighbor && neighbor.Elevation <= erodibleElevation)
				{
					candidates.Add(neighbor);
				}
			}

			// Choose random target from candidate list
			HexCell target = candidates[Random.Range(0, candidates.Count)];
			
			// Return list to list pool
			ListPool<HexCell>.Add(candidates);

			return target;
		}

		private void CreateClimate()
        {
			climate.Clear();
			nextClimate.Clear();

			ClimateData initialData = new ClimateData();
			initialData.moisture = startingMoisture;
			ClimateData clearData = new ClimateData();
			for (int i = 0; i < cellCount; i++)
            {
				climate.Add(initialData);
				nextClimate.Add(clearData);
            }

			for (int cycle = 0; cycle < 40; cycle++)
			{
				for (int i = 0; i < cellCount; i++)
				{
					EvolveClimate(i);
				}

				// Swap the two climate lists each cycle
				List<ClimateData> swap = climate;
				climate = nextClimate;
				nextClimate = swap;
			}
		}

		private void EvolveClimate(int cellIndex)
        {
			HexCell cell = grid.GetCell(cellIndex);
			ClimateData cellClimate = climate[cellIndex];

			// Evaporate water from moisture to clouds
			if (cell.IsUnderwater)
            {
				cellClimate.moisture = 1f;
				cellClimate.clouds += evaporationFactor;
            }
            else
            {
				float evaporation = cellClimate.moisture * evaporationFactor;
				cellClimate.moisture -= evaporation;
				cellClimate.clouds += evaporation;
			}

			// Precipitate water from clouds to moisture
			float precipitation = cellClimate.clouds * precipitationFactor;
			cellClimate.clouds -= precipitation;
			cellClimate.moisture += precipitation;

			float cloudMaximum = 1f - cell.ViewElevation / (elevationMaximum + 1f);
			// If there is more clouds than allowed, precipitate
			if (cellClimate.clouds > cloudMaximum)
			{
				cellClimate.moisture += cellClimate.clouds - cloudMaximum;
				cellClimate.clouds = cloudMaximum;
			}


			// Disperse clouds and moisture
			HexDirection mainDispersalDirection = windDirection.Opposite();
			float cloudDispersal = cellClimate.clouds * (1f / (5f + windStrength));
			float runoff = cellClimate.moisture * runoffFactor * (1f / 6f);
			float seepage = cellClimate.moisture * seepageFactor * (1f / 6f);
			for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
			{
				HexCell neighbor = cell.GetNeighbor(d);
				if (!neighbor)
				{
					continue;
				}
				
				ClimateData neighborClimate = nextClimate[neighbor.Index];

				// Disperse clouds
				if (d == mainDispersalDirection)
				{
					neighborClimate.clouds += cloudDispersal * windStrength;
				}
				else
				{
					neighborClimate.clouds += cloudDispersal;
				}
				//Disperse moisture
				int elevationDelta = neighbor.ViewElevation - cell.ViewElevation;
				// If neighbor is lower, then runoff
				if (elevationDelta < 0)
                {
					cellClimate.moisture -= runoff;
					neighborClimate.moisture += runoff;
                }
				// If neighbor is the same elevation, then seepage
				else if (elevationDelta == 0)
                {
					cellClimate.moisture -= seepage;
					neighborClimate.moisture += seepage;
                }

				nextClimate[neighbor.Index] = neighborClimate;
			}

			ClimateData nextCellClimate = nextClimate[cellIndex];
			nextCellClimate.moisture += cellClimate.moisture;
			// Limit moisture to 1
			if (nextCellClimate.moisture > 1f)
			{
				nextCellClimate.moisture = 1f;
			}

			nextClimate[cellIndex] = nextCellClimate;
			climate[cellIndex] = new ClimateData();
		}


		private void CreateRivers()
        {
			List<HexCell> riverOrigins = ListPool<HexCell>.Get();

			// Determine a weight for each cell
			for (int i = 0; i < cellCount; i++)
			{
				HexCell cell = grid.GetCell(i);
				if (cell.IsUnderwater)
				{
					continue;
				}
				ClimateData data = climate[i];
				float weight =
					data.moisture * (cell.Elevation - waterLevel) /
					(elevationMaximum - waterLevel);
				if (weight > 0.75f)
				{
					riverOrigins.Add(cell);
					riverOrigins.Add(cell);
				}
				if (weight > 0.5f)
				{
					riverOrigins.Add(cell);
				}
				if (weight > 0.25f)
				{
					riverOrigins.Add(cell);
				}
			}

			int riverBudget = Mathf.RoundToInt(landCells * riverPercentage * 0.01f);

			// Select river origin cells
			while (riverBudget > 0 && riverOrigins.Count > 0)
			{
				int index = Random.Range(0, riverOrigins.Count);
				int lastIndex = riverOrigins.Count - 1;
				HexCell origin = riverOrigins[index];
				riverOrigins[index] = riverOrigins[lastIndex];
				riverOrigins.RemoveAt(lastIndex);

				// Check to create the river
				if (!origin.HasRiver)
                {
					bool isValidOrigin = true;

					for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
					{
						HexCell neighbor = origin.GetNeighbor(d);

						// Don't place river origin next to existing river or water
						if (neighbor && (neighbor.HasRiver || neighbor.IsUnderwater))
						{
							isValidOrigin = false;
							break;
						}
					}

					if (isValidOrigin)
					{
						riverBudget -= CreateRiver(origin);
					}
				}

			}

			if (riverBudget > 0)
			{
				Debug.LogWarning("Failed to use up river budget.");
			}


			ListPool<HexCell>.Add(riverOrigins);
		}

		private int CreateRiver(HexCell origin)
		{
			int length = 1;

			HexCell cell = origin;
			HexDirection direction = HexDirection.NE;

			while (!cell.IsUnderwater)
            {
				int minNeighborElevation = int.MaxValue;
				flowDirections.Clear();
				
				for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
				{
					HexCell neighbor = cell.GetNeighbor(d);

					if (!neighbor)
					{
						continue;
					}

					// Set min neighbor elevation which helps place lakes
					if (neighbor.Elevation < minNeighborElevation)
					{
						minNeighborElevation = neighbor.Elevation;
					}

					// If neighbor already has river, exit
					if (neighbor == origin || neighbor.HasIncomingRiver)
					{
						continue;
					}

					// Skip neighbor cell if it is uphill
					int delta = neighbor.Elevation - cell.Elevation;
					if (delta > 0)
                    {
						continue;
                    }

					// If the neighbor has and outgoing river but no incoming river,
					// then it is an origin point, merge the rivers and return
					if (neighbor.HasOutgoingRiver)
					{
						cell.SetOutgoingRiver(d);
						return length;
					}

					// If downhill 'weight' the cell list with the neighbor tile
					if (delta < 0)
					{
						flowDirections.Add(d);
						flowDirections.Add(d);
						flowDirections.Add(d);
					}

					// If not multiple sharp turns in a row,
					// 'weight' the cell list with the neighbor tile
					if (length == 1 ||
						(d != direction.Next2() && d != direction.Previous2()))
                    {
						flowDirections.Add(d);
                    }

					flowDirections.Add(d);
				}

				// If no place to flow to, make lake or exit
				if (flowDirections.Count == 0)
				{
					// If river is only 1 long, return 0
					if (length == 1)
					{
						return 0;
					}

					if (minNeighborElevation >= cell.Elevation)
					{
						cell.WaterLevel = minNeighborElevation;
						if (minNeighborElevation == cell.Elevation)
						{
							cell.Elevation = minNeighborElevation - 1;
						}
					}
					break;

				}

				direction = flowDirections[Random.Range(0, flowDirections.Count)];
				
				cell.SetOutgoingRiver(direction);
				length += 1;

				// Create extra lakes
				if (minNeighborElevation >= cell.Elevation &&
					Random.value < extraLakeProbability)
                {
					cell.WaterLevel = cell.Elevation;
					cell.Elevation -= 1;
                }

				cell = cell.GetNeighbor(direction);
            }


			return length;
		}

	}
}