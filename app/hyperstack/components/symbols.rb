# frozen_string_literal: true

# Symbol table row
class SymbolItem < HyperComponent
  param :symbol
  render(TR) do
    TH(scope: 'row') { @Symbol.id.to_s }
    TD { @Symbol.name.to_s }
    TD { @Symbol.symbol_hash.to_s }
    TD { @Symbol.created_at.to_s }
    TD { (@Symbol.size.to_f / 2**20).round(2).to_s }
    TD { @Symbol.path.to_s }
  end
end

# Table showing all symbols
class Symbols < HyperComponent
  param :current_page, default: 0, type: Integer

  render(DIV) do
    H1 { 'Debug Symbols' }

    Paginator(current_page: @CurrentPage,
              item_count: DebugSymbol.count,
              ref: set(:paginator)) {
      # the ref set doesn't seem to immediately return a value
      if @paginator
        ReactStrap.Table(:striped, :responsive, :hover) {
          THEAD {
            TR {
              TH { 'ID' }
              TH { 'Name' }
              TH { 'Hash' }
              TH { 'Created' }
              TH { 'Size (MiB)' }
              TH { 'Path' }
            }
          }

          TBODY {
            DebugSymbol.offset(@paginator.offset).take(@paginator.take_count).each do |symbol|
              SymbolItem(symbol: symbol)
            end
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate @CurrentPage = page
    }
  end
end
