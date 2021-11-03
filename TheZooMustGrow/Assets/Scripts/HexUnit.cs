using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TheZooMustGrow
{
    public class HexUnit : MonoBehaviour
    {
		public static HexUnit unitPrefab;

		List<HexCell> pathToTravel;

		const float travelSpeed = 4f;


		HexCell location;
		public HexCell Location
		{
			get
			{
				return location;
			}
			set
			{
				// Remove unit from cell
				if (location)
                {
					location.Unit = null;
                }

				location = value;
				value.Unit = this;
				transform.localPosition = value.Position;
			}
		}

		float orientation;
		public float Orientation
		{
			get
			{
				return orientation;
			}
			set
			{
				orientation = value;
				transform.localRotation = Quaternion.Euler(0f, value, 0f);
			}
		}

		
		void OnEnable()
		{
			// Make sure the unit is in the correct place if
			// recompiled during movement animation
			if (location)
			{
				transform.localPosition = location.Position;
			}
		}

		/// <summary>
		/// Updates the position of the unit.
		/// </summary>
		public void ValidateLocation()
		{
			transform.localPosition = location.Position;
		}

		/// <summary>
		/// Destroy the unit and remove properties.
		/// </summary>
		public void Die()
        {
			location.Unit = null;
			Destroy(gameObject);
        }

		public bool IsValidDestination(HexCell cell)
		{
			return !cell.IsUnderwater && !cell.Unit;
		}

		public void Travel(List<HexCell> path)
        {
			location = path[path.Count - 1];
			pathToTravel = path;
			StopAllCoroutines();
			StartCoroutine(TravelPath());
		}

		private IEnumerator TravelPath()
		{
			Vector3 a, b, c = pathToTravel[0].Position;

			float t = Time.deltaTime * travelSpeed;
			for (int i = 1; i < pathToTravel.Count; i++)
			{
				a = c;
				b = pathToTravel[i - 1].Position;
				c = (b + pathToTravel[i].Position) * 0.5f;

				for (; t < 1f; t += Time.deltaTime * travelSpeed)
				{
					transform.localPosition = Bezier.GetPoint(a, b, c, t);
					yield return null;
				}
				t -= 1f;
			}
			
			a = c;
			b = pathToTravel[pathToTravel.Count - 1].Position;
			c = b; 
			for (; t < 1f; t += Time.deltaTime * travelSpeed)
			{
				transform.localPosition = Bezier.GetPoint(a, b, c, t);
				yield return null;
			}

			// Make sure the final location is correct;
			transform.localPosition = location.Position;
		}

		private void OnDrawGizmos()
        {
            if (pathToTravel == null || pathToTravel.Count == 0) { return; }

			Vector3 a, b, c = pathToTravel[0].Position;

			for (int i = 1; i < pathToTravel.Count; i++)
			{
				a = c;
				b = pathToTravel[i - 1].Position;
				c = (b + pathToTravel[i].Position) * 0.5f;
				
				for (float t = 0f; t < 1f; t += 0.1f)
				{
					Gizmos.DrawSphere(Bezier.GetPoint(a, b, c, t), 2f);
				}
			}

			// For the last cell
			a = c;
			b = pathToTravel[pathToTravel.Count - 1].Position;
			c = b;
			for (float t = 0f; t < 1f; t += 0.1f)
			{
				Gizmos.DrawSphere(Bezier.GetPoint(a, b, c, t), 2f);
			}


		}



        public void Save(BinaryWriter writer)
		{
			location.coordinates.Save(writer);
			writer.Write(orientation);
		}

		public static void Load(BinaryReader reader, HexGrid grid)
		{
			HexCoordinates coordinates = HexCoordinates.Load(reader);
			float orientation = reader.ReadSingle();

			grid.AddUnit(Instantiate(unitPrefab),
						 grid.GetCell(coordinates),
						 orientation);
		}
	}
}