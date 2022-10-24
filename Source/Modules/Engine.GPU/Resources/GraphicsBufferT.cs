﻿using System;
using System.Collections.Concurrent;
using Vortice.Direct3D12;

namespace Engine.GPU
{
	public unsafe class GraphicsBuffer<T> : GraphicsBuffer, IDisposable where T : unmanaged
	{
		private List<BufferAllocation<T>> allocations = new();

		public GraphicsBuffer(int elementCount, int alignment = 1, bool hasCounter = false, bool isRaw = false) : base(elementCount * sizeof(T), sizeof(T), alignment, hasCounter, isRaw)
		{

		}

		/// <summary>
		/// Allocates space in the buffer and returns a handle.
		/// </summary>
		/// <param name="resizeList">The command list to be used for resizing, if necessary.</param>
		public BufferAllocation<T> Allocate(int count, CommandList resizeList = null)
		{
			lock (allocations)
			{
				BufferAllocation<T> alloc = null;

				for (long i = 0; i < Capacity; i++)
				{
					long goalStart = i;
					long goalEnd = i + count;

					bool blocked = false;
					for (int j = 0; j < allocations.Count; j++) // Loop through blocks that might obstruct this area.
					{
						long blockStart = allocations[j].Start;
						long blockEnd = blockStart + allocations[j].Count - 1;

						// Can already tell this block isn't in the way.
						if (blockStart > goalEnd || blockEnd < goalStart)
						{
							continue;
						}

						// Check if the goal area is obstructed by this block.
						blocked = (goalStart >= blockStart && goalStart <= blockEnd) // Starts inside block
							|| (goalEnd >= blockStart && goalEnd <= blockEnd) // Ends inside block
							|| (goalStart <= blockStart && goalEnd >= blockStart) // Overlaps block start
							|| (goalStart <= blockEnd && goalEnd >= blockEnd); // Overlaps block end

						// There's a block in the way, and we know where it ends. Skip past known blocked elements.
						if (blocked)
						{
							i = blockEnd;
							break;
						}
					}

					if (!blocked)
					{
						alloc = new BufferAllocation<T>(this)
						{
							Start = goalStart,
							Count = count
						};

						// Maintain order - allocations that start further in buffer should start further in list
						for (int j = allocations.Count - 1; j >= -1; j--)
						{
							if (j == -1)
							{
								allocations.Add(alloc);
								break;
							}
							else if (allocations[j].Start < alloc.Start)
							{
								allocations.Insert(j + 1, alloc);
								break;
							}
						}

						break;
					}
				}

				// Couldn't find a large enough block.
				if (alloc == null)
				{
					Resize((Capacity * 2) * sizeof(T));
					return Allocate(count);
				}

				return alloc;
			}
		}

		public void Free(BufferAllocation<T> handle)
		{
			lock (allocations)
			{
				allocations.Remove(handle);
			}
		}

		public void Compact(CommandList list)
		{
			// If there's nothing allocated, there's nothing to compact.
			if (allocations.Count < 1)
			{
				return;
			}

			// Compact first element first
			if (allocations[0].Start != 0)
			{
				list.CopyBuffer(this, allocations[0].Start * sizeof(T), 0, allocations[0].Count * sizeof(T));
				allocations[0].Start = 0;
			}

			for (int i = 1; i < allocations.Count; i++)
			{
				var alloc = allocations[i];
				var prevAlloc = allocations[i - 1];
				
				// Free space between these allocations.
				if (alloc.Start != prevAlloc.End)
				{
					list.CopyBuffer(this, alloc.Start * sizeof(T), prevAlloc.End * sizeof(T), alloc.Count * sizeof(T));
					alloc.Start = prevAlloc.End;
				}
			}
		}

		public void Clear()
		{
			lock (allocations)
			{
				allocations.Clear();
			}
		}
	}

	public class BufferAllocation<T> : IDisposable where T : unmanaged
	{
		public long Start = 0;
		public long Count = 0;
		public long End => Start + Count;
		public GraphicsBuffer<T> Buffer { get; private set; }

		public BufferAllocation(GraphicsBuffer<T> source)
		{
			Buffer = source;
		}

		public void Dispose()
		{
			Buffer.Free(this);
		}
	}
}