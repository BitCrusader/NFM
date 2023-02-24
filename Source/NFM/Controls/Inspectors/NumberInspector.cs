﻿using System;
using System.ComponentModel;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.LogicalTree;

namespace NFM;

[CustomInspector(typeof(INumber<>))]
public partial class NumberInspector : UserControl
{
	public NumberInspector()
	{
		DataContext = this;

		// Create NumInput and bind to inspected property.
		Content = new NumInput()
		{
			[!NumInput.ValueProperty] = new Binding(nameof(Value))
		};
	}
}