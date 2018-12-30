class SymbolItem < HyperComponent
  param :symbol
  render(TR) do
    TD{"#{@Symbol.id}"}
    TD{"#{@Symbol.name}"}
    TD{"#{@Symbol.symbol_hash}"}
    TD{"#{@Symbol.created_at}"}
    TD{"#{(@Symbol.size.to_f / 2**20).round(2)}"}
    TD{"#{@Symbol.path}"}
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
          TD{ "Created" }
          TD{ "Size (MiB)" }
          TD{ "Path" }
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
