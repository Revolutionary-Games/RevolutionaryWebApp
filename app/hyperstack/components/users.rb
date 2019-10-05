class UserItem < HyperComponent
  include Hyperstack::Router::Helpers
  
  param :user
  render(TR) do
    TD{"#{@User.id}"}
    TD{Link("/user/#{@User.id}") { "#{@User.email}"}}
    TD{"#{@User.name}"}
    TD{"#{@User.local}"}
    TD{"#{@User.sso_source}"}
    TD{"#{@User.developer}"}
    TD{"#{@User.admin}"}
    TD{"#{@User.created_at}"}
    TD{@User.has_api_token}
    TD{@User.has_lfs_token}
  end
end

class Users < HyperComponent

  render(DIV) do
    H1 { "Users" }

    H2 { "Number of Users: #{User.sort_by_created_at.count}" }

    TABLE {

      THEAD {
        TR{
          TD{ "ID" }
          TD{ "Email" }
          TD{ "Name" }
          TD{ "Local" }
          TD{ "SSO" }
          TD{ "Developer?" }
          TD{ "Admin?" }
          TD{ "Created" }
          TD{ "Has API token?" }
          TD{ "Has Git LFS token?" }
        }
      }

      TBODY{
        User.sort_by_created_at.each do |user|
          UserItem(user: user)
        end
      }
    }

  end
end
