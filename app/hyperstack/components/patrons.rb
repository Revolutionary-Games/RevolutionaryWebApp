# frozen_string_literal: true

# Patron table row
class PatronItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :patron
  render(TR) do
    TH(scope: 'row') { @Patron.email.to_s }
    TD { @Patron.suspended.to_s }
    TD { @Patron.username.to_s }
    TD { @Patron.pledge }
    TD { @Patron.email_alias.to_s }
    TD { @Patron.has_patreon_token? }
  end
end

# List of all patrons
class Patrons < HyperComponent
  include Hyperstack::Router::Helpers

  param :current_page, default: 0, type: Integer

  def items
    Patron.sort_by_created_at
  end

  render(DIV) do
    H3 { 'Patrons' }

    Paginator(current_page: @CurrentPage,
              item_count: items.count,
              show_totals: true,
              ref: set(:paginator)) {
      # This is set with a delay
      if @paginator
        RS.Table(:striped, :responsive) {
          THEAD {
            TR {
              TH { 'Email' }
              TH { 'Suspended' }
              TH { 'Username' }
              TH { 'Pledge' }
              TH { 'Alias' }
              TH { 'Patreon login?' }
            }
          }

          TBODY {
            items.offset(@paginator.offset).take(@paginator.take_count).each { |patron|
              PatronItem(patron: patron)
            }
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate @CurrentPage = page
    }
  end
end
