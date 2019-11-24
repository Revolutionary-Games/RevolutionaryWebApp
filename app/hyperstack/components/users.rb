# frozen_string_literal: true

# User table row
class UserItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :user
  render(TR) do
    TH(scope: 'row') { @User.id.to_s }
    TD { Link("/user/#{@User.id}") { @User.email.to_s } }
    TD { @User.name.to_s }
    TD { @User.local.to_s }
    TD { @User.sso_source.to_s }
    TD { @User.developer.to_s }
    TD { @User.admin.to_s }
    TD { @User.created_at.to_s }
    TD { @User.has_api_token }
    TD { @User.has_lfs_token }
  end
end

# List of all users
class Users < HyperComponent
  param :current_page, default: 0, type: Integer

  def items
    User.sort_by_created_at
  end

  render(DIV) do
    H1 { 'Users' }

    Paginator(current_page: @CurrentPage,
              item_count: items.count,
              show_totals: true,
              ref: set(:paginator)) {
      # This is set with a delay
      if @paginator
        RS.Table(:striped, :responsive) {
          THEAD {
            TR {
              TH { 'ID' }
              TH { 'Email' }
              TH { 'Name' }
              TH { 'Local' }
              TH { 'SSO' }
              TH { 'Developer?' }
              TH { 'Admin?' }
              TH { 'Created' }
              TH { 'Has API token?' }
              TH { 'Has Git LFS token?' }
            }
          }

          TBODY {
            items.offset(@paginator.offset).take(@paginator.take_count).each { |user|
              UserItem(user: user)
            }
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate @CurrentPage = page
    }
  end
end
