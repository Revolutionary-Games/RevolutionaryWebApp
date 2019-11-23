# frozen_string_literal: true

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

class Symbols < HyperComponent
  param :current_page, default: 0, type: Integer
  param :page_size, default: 50, type: Integer

  render(DIV) do
    H1 { 'Debug Symbols' }

    # paginator =
    Paginator(current_page: @CurrentPage,
              page_size: @PageSize,
              item_count: DebugSymbol.count,
              on_page_changed: lambda { |page|
                                 mutate @CurrentPage = page
                               })

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
        # DebugSymbol.offset(paginator.offset).take(paginator.take_count).each do |symbol|
        DebugSymbol.offset(@CurrentPage * @PageSize).take(@PageSize).each do |symbol|
          SymbolItem(symbol: symbol)
        end
      }
    }
  end
end
