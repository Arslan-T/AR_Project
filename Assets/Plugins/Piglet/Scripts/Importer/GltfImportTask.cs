using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Piglet
{
	/// <summary>
	/// Sequentially executes a set of subtasks (coroutines) in order
	/// to import a glTF model. Each subtask corresponds to importing a
	/// different type of glTF entity (buffers, textures, materials,
	/// meshes, etc.).
	///
	/// In principle, this class could be replaced by a simple wrapper
	/// coroutine method that iterates through the subtask coroutines in
	/// sequence.  However, this class provides the additional abilities
	/// to: (1) abort the import process, (2) specify user-defined
	/// callbacks for abortion/exception/completion, and (3) check the
	/// current execution state of the import task
	/// (running/aborted/exception/completed).
	/// </summary>
	public class GltfImportTask : IEnumerator
	{
		/// <summary>
		/// The possible execution states of an import task.
		/// </summary>
		public enum ExecutionState
		{
			Running,
			Aborted,
			Exception,
			Completed
		};

		/// <summary>
		/// The current execution state of this import task (e.g. aborted).
		/// </summary>
		public ExecutionState State;

		/// <summary>
		/// Callback(s) that are invoked to report
		/// intermediate progress during a glTF import.
		/// </summary>
		public GltfImporter.ProgressCallback OnProgress;

		/// <summary>
		/// Prototype for callbacks that are invoked when
		/// a glTF import is aborted by the user.
		/// </summary>
		public delegate void AbortedCallback();

		/// <summary>
		/// Callback(s) that are invoked when the glTF import is
		/// aborted by the user. This provides a
		/// useful hook for cleaning up the aborted import task.
		/// </summary>
		public AbortedCallback OnAborted;

		/// <summary>
		/// Prototype for callbacks that are invoked when
		/// an exception occurs during a glTF import.
		/// </summary>
		public delegate void ExceptionCallback(Exception e);

		/// <summary>
		/// Callback(s) that are invoked when an exception
		/// is thrown during a glTF import. This provides a
		/// useful hook for cleaning up a failed import task
		/// and/or presenting error messages to the user.
		/// </summary>
		public ExceptionCallback OnException;

		/// <summary>
		/// If true, an exception will be rethrown after
		/// being passed to user-defined exception callbacks
		/// in OnException.
		/// </summary>
		public bool RethrowExceptionAfterCallbacks;

		/// <summary>
		/// Prototype for callbacks that are invoked when
		/// the glTF import task has successfully completed.
		/// </summary>
		public delegate void CompletedCallback(GameObject importedModel);

		/// <summary>
		/// Callback(s) that are invoked when the glTF import
		/// successfully completes.  The root GameObject of
		/// the imported model is passed as argument to these
		/// callbacks.
		/// </summary>
		public CompletedCallback OnCompleted;

		/// <summary>
		/// The list of subtasks (coroutines) that make up
		/// the overall glTF import task.
		/// </summary>
		List<IEnumerator> _tasks;

		/// <summary>
		/// Maximum number of milliseconds that MoveNext() should execute
		/// before returning control back to the main Unity thread.
		/// </summary>
		public int MillisecondsPerYield;

		/// <summary>
		/// Stopwatch used to track time spent in MoveNext(). We
		/// do as much work as possible per frame, but stop as
		/// soon as we exceed MillisecondsPerYield. This prevents
		/// unnecessarily stalling of the main Unity thread
		/// during glTF imports (i.e. frame rate drops).
		/// </summary>
		private Stopwatch _stopwatch;

		/// <summary>
		/// The longest invocation of this class's MoveNext() method
		/// across the entire glTF import.
		/// </summary>
		public long LongestMoveNextInMilliseconds;

		/// <summary>
		/// Statistics about the time spent executing a import task
		/// (e.g. GltfImporter.LoadTextures).
		/// </summary>
		public class ProfilingRecord
		{
			/// <summary>
			/// The type of the IEnumerator that
			/// we called MoveNext() on.
			///
			/// By happy circumstance, the type of
			/// the IEnumerator contains the name of the
			/// method that generated the IEnumerator, and
			/// this allows us to distinguish between the
			/// different import subtasks (e.g. LoadTextures)
			/// in the reported profiling data.
			///
			/// This is a hack that is dependent
			/// on the particular type names that Mono
			/// auto-generates for IEnumerators, but it is
			/// a nice shortcut and works well enough for now.
			/// </summary>
			public Type TaskType;

			/// <summary>
			/// Records the time spent executing this task's MoveNext() method.
			/// This result excludes any time spent in Unity's main
			/// game loop between calls to MoveNext().
			/// </summary>
			public Stopwatch StopwatchMoveNext;

			/// <summary>
			/// Records the total wallclock time spent executing this task,
			/// between the first and last calls to MoveNext(). This result
			/// includes any time spent in Unity's main game loop
			/// between calls to MoveNext().
			/// </summary>
			public Stopwatch StopwatchWallclock;

			/// <summary>
			/// Number of calls to this task's MoveNext() method.
			/// </summary>
			public long MoveNextCalls;

			/// <summary>
			/// Number of frames to execute this task. More
			/// precisely, the number of GltfImportTask.MoveNext()
			/// calls used to execute this task.
			/// </summary>
			public long Frames;

			public ProfilingRecord(Type taskType)
			{
				TaskType = taskType;
				StopwatchMoveNext = new Stopwatch();
				StopwatchWallclock = new Stopwatch();
				MoveNextCalls = 0;
				Frames = 0;
			}
		}

		/// <summary>
		/// Profiling data recorded for each import task (coroutine).
		/// </summary>
		public List<ProfilingRecord> ProfilingRecords;

		public GltfImportTask()
		{
			_tasks = new List<IEnumerator>();
			_stopwatch = new Stopwatch();
			LongestMoveNextInMilliseconds = 0;
			ProfilingRecords = new List<ProfilingRecord>();

			State = ExecutionState.Running;
			RethrowExceptionAfterCallbacks = true;

			// For runtime glTF imports, target 60 fps (16 ms per frame).
			// For Editor imports, run the whole task in one MoveNext call
			// to minimize the overall import time.
			MillisecondsPerYield = Application.isPlaying ? 16 : 100;
		}

		/// <summary>
		/// Add a subtask to the front of the subtask list.
		/// </summary>
		public void PushTask(IEnumerator task)
		{
			_tasks.Insert(0, task);
		}

		/// <summary>
		/// Add a subtask to the front of the subtask list.
		/// </summary>
		public void PushTask(Action action)
		{
			IEnumerator ActionWrapper()
			{
				action.Invoke();
				yield return null;
			}

			_tasks.Insert(0, ActionWrapper());
		}

		/// <summary>
		/// Add a subtask to be executed during the import process.
		/// Subtasks are typically used for importing different
		/// types of glTF entities (e.g. buffers, textures, meshes).
		/// Subtasks are executed in the order that they are added.
		/// </summary>
		public void AddTask(IEnumerator task)
		{
			_tasks.Add(task);
		}

		/// <summary>
		/// Add a subtask to be executed during the import process.
		/// Subtasks are typically used for importing different
		/// types of glTF entities (e.g. buffers, textures, meshes).
		/// Subtasks are executed in the order that they are added.
		/// </summary>
		public void AddTask(IEnumerable task)
		{
			_tasks.Add(task.GetEnumerator());
		}

		/// <summary>
		/// Add a subtask for a C# Action (i.e. a method with zero arguments
		/// that does not return a value).
		/// </summary>
		public void AddTask(Action action)
		{
			IEnumerator ActionWrapper()
			{
				action.Invoke();
				yield return null;
			}

			AddTask(ActionWrapper());
		}

		/// <summary>
		/// Abort this import task.
		/// </summary>
		public void Abort()
		{
			State = ExecutionState.Aborted;
			OnAborted?.Invoke();
			Clear();
		}

		/// <summary>
		/// Clear the list of subtasks.
		/// </summary>
		public void Clear()
		{
			_tasks.Clear();
		}

		/// <summary>
		/// Advance execution of the current subtask by a single step.
		/// </summary>
		public bool MoveNext()
		{
			if (State != ExecutionState.Running)
				return false;

			bool moveNextOuter = false;
			try
			{
				// Tracks how long we spend in this method (GltfImportTask.MoveNext).
				// We want to do as much work as possible per frame but we need to
				// stop as soon as we exceed the MillisecondsPerYield limit (to
				// avoid stalling the main Unity thread).

				_stopwatch.Restart();

				// If we have not yet call MoveNext() on first task, we need
				// to create a profiling record for it.

				if (_tasks.Count > 0 && ProfilingRecords.Count == 0)
				{
					ProfilingRecords.Add(new ProfilingRecord(_tasks[0].GetType()));
					ProfilingRecords.Last().StopwatchWallclock.Start();
				}

				ProfilingRecords.Last().Frames++;

				while (_tasks.Count > 0 &&
				       (_stopwatch.ElapsedMilliseconds < MillisecondsPerYield || !moveNextOuter))
				{
					ProfilingRecords.Last().StopwatchMoveNext.Start();
					var moveNextInner = _tasks[0].MoveNext();
					ProfilingRecords.Last().StopwatchMoveNext.Stop();
					ProfilingRecords.Last().MoveNextCalls++;

					moveNextOuter |= moveNextInner;

					if (moveNextInner
					    && Current is YieldType yieldType
					    && yieldType == YieldType.Blocked)
					{
						break;
					}

					if (!moveNextInner)
					{
						// stop profiling the completed task
						ProfilingRecords.Last().StopwatchWallclock.Stop();

						// if we just completed the last task
						if (_tasks.Count == 1)
						{
							State = ExecutionState.Completed;
							OnCompleted?.Invoke((GameObject)Current);
						}

						// remove completed task
						_tasks.RemoveAt(0);

						// if there is a next task, start profiling it
						if (_tasks.Count > 0)
						{
							ProfilingRecords.Add(new ProfilingRecord(_tasks[0].GetType()));
							ProfilingRecords.Last().StopwatchWallclock.Start();
							ProfilingRecords.Last().Frames++;
						}
					}
				}

				_stopwatch.Stop();

				if (_stopwatch.ElapsedMilliseconds > LongestMoveNextInMilliseconds)
					LongestMoveNextInMilliseconds = _stopwatch.ElapsedMilliseconds;

			}
			catch (Exception e)
			{
				State = ExecutionState.Exception;
				OnException?.Invoke(e);
				Clear();

				if (RethrowExceptionAfterCallbacks)
					throw;

				return false;
			}

			return moveNextOuter;
		}

		/// <summary>
		/// <para>
		/// This method is a stub and always throws
		/// NotImplementedException().
		/// </para>
		/// <para>
		/// The `Reset()` method is required by the IEnumerator
		/// interface, but does not serve any useful purpose
		/// for this particular class (`GltfImportTask`).
		/// </para>
		/// </summary>
		public void Reset()
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// <para>
		/// The last value returned by `yield return` for the
		/// currently executing subtask (coroutine).
		/// </para>
		/// <para>
		/// The value of `Current` is generally not of
		/// interest to Piglet users until the entire glTF import
		/// has completed successfully, in which case `Current`
		/// is a reference to the root `GameObject` of the imported
		/// model.
		/// </para>
		/// </summary>
		public object Current
		{
			get
			{
				if (_tasks.Count == 0)
					return null;

				return _tasks[0].Current;
			}
		}
	}
}