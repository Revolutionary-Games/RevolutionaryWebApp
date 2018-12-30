class UserItem < HyperComponent
  param :user
  render(TR) do
    TD{"#{@User.id}"}
    TD{"#{@User.email}"}
    TD{"#{@User.name}"}
    TD{"#{@User.local}"}
    TD{"#{@User.sso_source}"}
    TD{"#{@User.developer}"}
    TD{"#{@User.admin}"}
    TD{"#{@User.created_at}"}
    TD{@User.has_api_token}
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
