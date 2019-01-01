class DebugSymbolPolicy
  
  # Developers get debug symbol info
  regulate_class_connection { developer? }

  # Just send everything
  regulate_broadcast { |policy|
    policy.send_all().to(DebugSymbol)
  }
end
