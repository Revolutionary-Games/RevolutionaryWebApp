class UserItem < HyperComponent
  param :user
  render(TR) do
    TD{@User.id}
    TD{@User.email}
    TD{@User.name}
    TD{@User.local}
    TD{@User.sso_source}
    TD{@User.developer}
    TD{@User.admin}
    TD{@User.created_at}
    TD{!@User.api_token.blank?}
  end
end

class Users < HyperComponent

  render(DIV) do
    H1 { "Users" }

    H2 { "Number of Users: #{User.count}" }

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
        User.each do |user|
          UserItem(user: user)
        end
      }
    }

  end
end
