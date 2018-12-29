class SymbolItem < HyperComponent
  param :symbol
  render(TR) do
    TD{"#{@Symbol.id}"}
    TD{"#{@Symbol.name}"}
    TD{"#{@Symbol.symbol_hash}"}
    TD{"#{@Symbol.path}"}
    TD{"#{@Symbol.created_at}"}
  end
end

class Symbols < HyperComponent

  render(DIV) do

    H1 { "Debug Symbols" }

    DIV { "Total symbols: #{DebugSymbol.count}" }

    TABLE {

      THEAD {
        TR{
          TD{ "ID" }
          TD{ "Name" }
          TD{ "Hash" }
          TD{ "Path" }
          TD{ "Created" }
        }
      }

      TBODY{
        DebugSymbol.each do |symbol|
          SymbolItem(symbol: symbol)
        end
      }
    }

  end

end
